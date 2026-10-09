using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Lexi;

public partial class MainWindow
{
    private void OpenPlanCreator(DailyStudyPlanSource source, LearningSection? initialSection = null)
    {
        if (_learningPlansLoadFailed || !_databaseAvailable || _restoring)
        {
            SetStatus("计划数据暂不可用，请先完成恢复。");
            return;
        }

        if (source == DailyStudyPlanSource.Ielts)
        {
            try { _ieltsCatalog ??= IeltsCatalog.Load(); }
            catch (Exception ex) { SetStatus("IELTS 资源加载失败：" + ex.Message); return; }
        }

        var sections = _ieltsCatalog?.Sections.Where(s => s.Kind == "vocabulary").ToList() ?? [];
        if (source == DailyStudyPlanSource.Ielts && sections.Count == 0)
        {
            SetStatus("IELTS 词汇目录为空。");
            return;
        }

        var archiveWords = _allWords.Select(w => new DailyStudyPlanWord
        {
            Id = w.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Word = w.Word,
            Meaning = w.Translation, Phonetic = w.Phonetic, Definition = w.Definition,
            Example = string.Join("\n", w.AiExamples.Select(e => e.English + "\n" + e.Chinese))
        }).ToList();
        var selected = new HashSet<string>(StringComparer.Ordinal);
        if (source == DailyStudyPlanSource.Archive)
        {
            var marked = _allWords.Where(w => w.Selected).Select(w => w.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            selected.UnionWith(marked);
            if (selected.Count == 0) selected.UnionWith(archiveWords.Select(w => w.Id));
        }
        else
        {
            if (initialSection == null || !sections.Contains(initialSection)) initialSection = sections[0];
            var availableIds = sections.SelectMany(s => s.Entries).Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
            selected.UnionWith(_ieltsSelected.Where(availableIds.Contains));
            if (selected.Count == 0) selected.UnionWith(initialSection.Entries.Select(w => w.Id));
        }

        var dialog = new Window
        {
            Title = "创建每日学习计划", Width = 620, Height = 730,
            MinWidth = 460, MinHeight = 540, MaxWidth = 760,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true, Background = RootWindowBorder.Background,
            RequestedThemeVariant = RequestedThemeVariant
        };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24), RowSpacing = 14 };
        var header = new StackPanel { Spacing = 12 };
        header.Children.Add(LearningText("创建每日学习计划", 22));
        ComboBox? sectionPicker = null;
        if (source == DailyStudyPlanSource.Ielts)
        {
            sectionPicker = new ComboBox { ItemsSource = sections, SelectedItem = initialSection, HorizontalAlignment = HorizontalAlignment.Stretch };
            header.Children.Add(sectionPicker);
        }
        var search = new TextBox { Name = "PlanCreatorSearch", Watermark = "搜索计划词条" };
        header.Children.Add(search);
        var selectionActions = new WrapPanel();
        var selectAll = LearningButton("全选", () => { });
        var clear = LearningButton("清空选择", () => { });
        selectionActions.Children.Add(selectAll);
        selectionActions.Children.Add(clear);
        header.Children.Add(selectionActions);
        Grid.SetRow(header, 0); root.Children.Add(header);

        var rows = new StackPanel { Spacing = 6 };
        var pageLabel = LearningText("");
        var previous = LearningButton("上一页", () => { });
        var next = LearningButton("下一页", () => { });
        var pager = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        pager.Children.Add(previous); pager.Children.Add(pageLabel); pager.Children.Add(next);
        var list = new StackPanel { Spacing = 12 };
        list.Children.Add(new ScrollViewer
        {
            Content = rows, MaxHeight = 340,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden
        });
        list.Children.Add(pager);
        Grid.SetRow(list, 1); root.Children.Add(list);

        var footer = new StackPanel { Spacing = 10 };
        var selectedLabel = LearningText("");
        var planName = new TextBox { Name = "PlanCreatorName", Watermark = "计划名称", Text = source == DailyStudyPlanSource.Ielts ? "我的 IELTS 计划" : "我的词汇计划" };
        var quotaLabel = LearningText("每日学习词数");
        var quota = new NumericUpDown { Name = "PlanCreatorQuota", Minimum = 1, Maximum = 10000, Value = 20, Increment = 1, FormatString = "0" };
        var random = new CheckBox { Name = "PlanCreatorRandom", Content = "随机顺序", IsChecked = false };
        var estimate = LearningText("");
        var error = LearningText("");
        var cancel = LearningButton("取消", dialog.Close);
        var confirm = LearningButton("创建计划", () => { }, true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(confirm);
        footer.Children.Add(selectedLabel); footer.Children.Add(planName);
        footer.Children.Add(quotaLabel); footer.Children.Add(quota);
        footer.Children.Add(random); footer.Children.Add(estimate); footer.Children.Add(error); footer.Children.Add(buttons);
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        dialog.Content = root;

        var page = 0;
        var overlapConfirmed = false;
        IReadOnlyList<DailyStudyPlanWord> CurrentWords()
        {
            if (source == DailyStudyPlanSource.Archive) return archiveWords;
            return (sectionPicker?.SelectedItem as LearningSection)?.Entries.Select(ToPlanWord).ToList() ?? [];
        }
        List<DailyStudyPlanWord> Filtered() => CurrentWords()
            .Where(w => string.IsNullOrWhiteSpace(search.Text) ||
                w.Word.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) ||
                w.Meaning.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        List<DailyStudyPlanWord> SelectedWords()
        {
            var sourceWords = source == DailyStudyPlanSource.Archive
                ? archiveWords : sections.SelectMany(s => s.Entries).Select(ToPlanWord).ToList();
            return sourceWords.Where(w => selected.Contains(w.Id)).DistinctBy(w => w.Id).ToList();
        }
        void RefreshSummary()
        {
            var count = SelectedWords().Count;
            var daily = (int)(quota.Value ?? 20);
            selectedLabel.Text = $"{(source == DailyStudyPlanSource.Archive ? "词汇档案" : "IELTS 专题")} · 已选 {count} 词";
            estimate.Text = $"每日 {daily} 词 · 预计 {((count + daily - 1) / daily)} 天完成";
            confirm.IsEnabled = count > 0;
        }
        void RenderPage()
        {
            rows.Children.Clear();
            var filtered = Filtered();
            var pages = Math.Max(1, (filtered.Count + 7) / 8);
            page = Math.Clamp(page, 0, pages - 1);
            foreach (var word in filtered.Skip(page * 8).Take(8))
            {
                var check = new CheckBox { Content = $"{word.Word} · {word.Meaning}", IsChecked = selected.Contains(word.Id) };
                check.IsCheckedChanged += (_, _) =>
                {
                    if (check.IsChecked == true) selected.Add(word.Id); else selected.Remove(word.Id);
                    overlapConfirmed = false; error.Text = ""; RefreshSummary();
                };
                rows.Children.Add(check);
            }
            if (filtered.Count == 0) rows.Children.Add(LearningText("没有匹配词条。"));
            pageLabel.Text = $"{page + 1} / {pages}";
            previous.IsEnabled = page > 0; next.IsEnabled = page < pages - 1;
            RefreshSummary();
        }
        selectAll.Click += (_, _) => { selected.UnionWith(Filtered().Select(w => w.Id)); overlapConfirmed = false; error.Text = ""; RenderPage(); };
        clear.Click += (_, _) => { selected.ExceptWith(Filtered().Select(w => w.Id)); overlapConfirmed = false; error.Text = ""; RenderPage(); };
        previous.Click += (_, _) => { page--; RenderPage(); };
        next.Click += (_, _) => { page++; RenderPage(); };
        search.TextChanged += (_, _) => { page = 0; RenderPage(); };
        if (sectionPicker != null) sectionPicker.SelectionChanged += (_, _) => { page = 0; RenderPage(); };
        quota.ValueChanged += (_, _) => { overlapConfirmed = false; error.Text = ""; RefreshSummary(); };
        planName.TextChanged += (_, _) => { overlapConfirmed = false; error.Text = ""; };
        confirm.Click += (_, _) =>
        {
            var words = SelectedWords();
            if (words.Count == 0 || string.IsNullOrWhiteSpace(planName.Text)) { error.Text = "请选择词条并输入计划名称。"; return; }
            var count = (int)(quota.Value ?? 0);
            if (count is < 1 or > 10000) { error.Text = "每日词数应为 1–10000。"; return; }
            var sourceLabel = source == DailyStudyPlanSource.Archive ? "词汇档案" : "IELTS 专题";
            var plan = DailyStudyPlanRules.Create(planName.Text, source, sourceLabel, words, count, random.IsChecked == true, Random.Shared.Next());
            var overlaps = DailyStudyPlanRules.FindOverlaps(_learningPlans, plan);
            if (overlaps.Count > 0 && !overlapConfirmed)
            {
                var names = string.Join("、", overlaps.Select(o => o.PlanName).Distinct().Take(3));
                var examples = string.Join("、", overlaps.Select(o => o.Word).Distinct().Take(4));
                error.Text = $"与 {names} 重叠 {overlaps.Select(o => o.Word).Distinct().Count()} 词（{examples}）。再次点击可继续创建。";
                overlapConfirmed = true;
                return;
            }
            var updated = _learningPlans.Append(plan).ToList();
            if (!SaveLearningPlans(updated)) { error.Text = "保存失败，请检查计划文件。"; return; }
            _learningPlans = updated; RenderLearningPlans();
            SetStatus($"计划已创建，共 {words.Count} 词。");
            dialog.Close();
        };
        RenderPage();
        _ = dialog.ShowDialog(this);
    }

    private static DailyStudyPlanWord ToPlanWord(LearningWord word) => new()
    {
        Id = word.Id, Word = word.Word, Meaning = word.Meaning, Phonetic = word.Phonetic,
        Definition = word.Extra, Example = word.Example, AudioPath = word.AudioPath
    };
}
