using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lexi.Features.Learning;
using Lexi.Features.Settings;

namespace Lexi;

public partial class MainWindow
{
    private Border? _planEditDrawer;

    private void RenderPlanBoardGroup(IEnumerable<DailyStudyPlan> plans, DateOnly today,
        IReadOnlySet<string> dueWordKeys)
    {
        var list = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var plan in plans)
        {
            var card = CreateLearningPlanPreviewCard(plan, today, dueWordKeys);
            AttachLearningPlanCardActivation(card, plan);
            list.Children.Add(card);
        }

        _learningPlansPanel.Children.Add(list);
    }

    private Border CreateLearningPlanPreviewCard(DailyStudyPlan plan, DateOnly today,
        IReadOnlySet<string> dueWordKeys)
    {
        var batch = DailyStudyPlanRules.GetTodayBatch(plan, today);
        var completedToday = DailyStudyPlanRules.TodayCompleted(plan, today);
        var firstLearnRemaining = batch.Count(word => !plan.CompletedWordIds.Contains(word.Id));
        var dueWords = GetPlanDueReviewWords(plan, dueWordKeys);
        var spellingWords = SpellingWordsForPlan(plan, today, dueWordKeys);
        var status = plan.Status switch
        {
            DailyStudyPlanStatus.Active when batch.Count > 0 && completedToday >= batch.Count =>
                UiText.Bilingual("今日完成", "Done today"),
            DailyStudyPlanStatus.Active => UiText.Bilingual("进行中", "In progress"),
            DailyStudyPlanStatus.Stopped => UiText.Bilingual("已停止", "Stopped"),
            _ => UiText.Bilingual("已完成", "Completed")
        };

        var information = new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = new TextBlock
        {
            Text = plan.Name,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(title, plan.Name);
        titleRow.Children.Add(title);

        var statusBadge = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        statusBadge.Classes.Add("status-pill");
        statusBadge.Bind(Border.BackgroundProperty, this.GetResourceObservable("TintBrush"));
        var statusText = new TextBlock
        {
            Text = status,
            FontSize = 11,
            FontWeight = FontWeight.Medium
        };
        statusText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        statusBadge.Child = statusText;
        Grid.SetColumn(statusBadge, 1);
        titleRow.Children.Add(statusBadge);
        information.Children.Add(titleRow);

        var source = new TextBlock
        {
            Text = PlanSourceDisplay(plan),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        };
        information.Children.Add(source);

        information.Children.Add(new TextBlock
        {
            Text = UiText.Bilingual(
                $"今日 {completedToday}/{batch.Count} 词 · 到期复习 {dueWords.Count} · 首学剩余 {firstLearnRemaining}",
                $"{completedToday}/{batch.Count} today · {dueWords.Count} due · {firstLearnRemaining} to learn"),
            FontSize = 11,
            Opacity = 0.66,
            TextWrapping = TextWrapping.Wrap
        });

        var overallLabels = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var overallLabel = LearningBilingualText("总体进度", "Overall progress", 11);
        overallLabel.Opacity = 0.64;
        overallLabels.Children.Add(overallLabel);
        var overallValue = new TextBlock
        {
            Text = $"{plan.CompletedWordIds.Count}/{plan.Words.Count}",
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(overallValue, 1);
        overallLabels.Children.Add(overallValue);

        var overallProgress = new ProgressBar
        {
            Minimum = 0,
            Maximum = Math.Max(1, plan.Words.Count),
            Value = Math.Min(plan.Words.Count, plan.CompletedWordIds.Count),
            Height = 5
        };
        overallProgress.Classes.Add("plan-progress");

        var remainingDays = DailyStudyPlanRules.EstimatedDaysRemaining(plan);
        var remainingText = new TextBlock
        {
            Text = UiText.Bilingual(
                $"预计剩余 {remainingDays} 天",
                $"{remainingDays} days estimated"),
            FontSize = 10,
            Opacity = 0.58,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var canFirstLearn = plan.Status == DailyStudyPlanStatus.Active && firstLearnRemaining > 0;
        var canReview = plan.Status != DailyStudyPlanStatus.Stopped && dueWords.Count > 0;
        var canSpell = plan.Status != DailyStudyPlanStatus.Stopped && spellingWords.Count > 0;
        actions.Children.Add(BuildPlanPreviewAction(
            "首次学习", "First learn",
            canFirstLearn ? () => StartDailyLearning(plan, StudyMode.FirstLearn) : null,
            primary: canFirstLearn));
        actions.Children.Add(BuildPlanPreviewAction(
            "复习", "Review",
            canReview ? () => StartDailyLearning(plan, StudyMode.Review) : null,
            primary: !canFirstLearn && canReview));

        var typingWords = spellingWords.Select(ToTypingWord).ToList();
        actions.Children.Add(BuildPlanPreviewAction(
            "淡写", "Hinted spelling",
            canSpell
                ? () => StartLearningTyping(typingWords, true,
                    returnPlanId: plan.Id)
                : null,
            primary: !canFirstLearn && !canReview && canSpell));
        actions.Children.Add(BuildPlanPreviewAction(
            "默写", "Dictation",
            canSpell
                ? () => StartLearningTyping(typingWords, false,
                    returnPlanId: plan.Id)
                : null));

        var moreButton = new Button
        {
            Content = "•••",
            Padding = new Thickness(8, 4),
            MinHeight = 32,
            Margin = new Thickness(0, 0, 7, 7),
            VerticalAlignment = VerticalAlignment.Center
        };
        moreButton.Classes.Add("secondary");
        ToolTip.SetTip(moreButton, UiText.Bilingual("计划管理", "Plan management"));
        moreButton.Click += (_, _) => ShowPlanManagementMenu(plan, moreButton);
        actions.Children.Add(moreButton);

        var actionArea = new StackPanel { Spacing = 7 };
        actionArea.VerticalAlignment = VerticalAlignment.Center;
        actionArea.Children.Add(overallLabels);
        actionArea.Children.Add(overallProgress);
        actionArea.Children.Add(remainingText);
        actionArea.Children.Add(actions);

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,360"),
            ColumnSpacing = 24
        };
        content.Children.Add(information);
        Grid.SetColumn(actionArea, 1);
        content.Children.Add(actionArea);
        var stacked = true;
        void ApplyLayout()
        {
            var nextStacked = content.Bounds.Width <= 0 || content.Bounds.Width < 840;
            if (stacked == nextStacked && content.RowDefinitions.Count > 0) return;
            stacked = nextStacked;
            content.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "*,360");
            content.RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "Auto");
            Grid.SetColumn(information, 0);
            Grid.SetRow(information, 0);
            Grid.SetColumn(actionArea, stacked ? 0 : 1);
            Grid.SetRow(actionArea, stacked ? 1 : 0);
            actionArea.Margin = new Thickness(0, stacked ? 10 : 0, 0, 0);
            actionArea.HorizontalAlignment = HorizontalAlignment.Stretch;
            actions.HorizontalAlignment = stacked
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right;
        }
        ApplyLayout();
        content.SizeChanged += (_, _) => ApplyLayout();

        var card = new Border
        {
            Child = content,
            Padding = new Thickness(18, 14),
            MinHeight = 116,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        card.Classes.Add("plan-card");
        card.Classes.Add("plan-row-card");
        return card;
    }

    private Button BuildPlanPreviewAction(string chinese, string english, Action? action,
        bool primary = false)
    {
        var button = LearningBilingualButton(chinese, english, action ?? (() => { }), primary);
        button.Padding = new Thickness(11, 7);
        button.MinHeight = 32;
        button.Margin = new Thickness(0, 0, 7, 7);
        button.FontSize = 11;
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.IsEnabled = action is not null;
        ToolTip.SetTip(button, action is null
            ? UiText.Bilingual("当前不可用", "Unavailable right now")
            : UiText.Bilingual($"开始{chinese}", $"Start {english.ToLowerInvariant()}"));
        return button;
    }

    private void AttachLearningPlanCardActivation(Control card, DailyStudyPlan plan)
    {
        if (card is not Border border) return;
        border.Focusable = true;
        try { border.Cursor = new Cursor(StandardCursorType.Hand); } catch { }

        border.PointerPressed += (_, e) =>
        {
            if (!IsLearningPlanCardActionSource(e.Source, border)) border.Focus();
        };
        border.PointerReleased += (_, e) =>
        {
            if (IsLearningPlanCardActionSource(e.Source, border)) return;
            e.Handled = true;
            OpenLearningPlanDetails(plan);
        };
        border.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space) ||
                IsLearningPlanCardActionSource(e.Source, border)) return;
            e.Handled = true;
            OpenLearningPlanDetails(plan);
        };
        border.GotFocus += (_, _) =>
            border.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AccentBrush"));
        border.LostFocus += (_, _) =>
            border.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));
    }

    private static bool IsLearningPlanCardActionSource(object? source, Control card)
    {
        for (var current = source as Control; current != null && !ReferenceEquals(current, card);
             current = current.Parent as Control)
        {
            if (current is Button or TextBox or ComboBox or Slider) return true;
        }
        return false;
    }

    private void ShowPlanManagementMenu(DailyStudyPlan plan,Control anchor)
    {
        var menu=new MenuFlyout();
        TrackStudyFlyout(menu);
        void Item(string chinese,string english,Action action)
        {
            var item=new MenuItem { Header=UiText.Bilingual(chinese,english) };
            item.Click+=(_,_)=>action();
            menu.Items.Add(item);
        }
        if(plan.Status==DailyStudyPlanStatus.Active)
        {
            Item("调整计划","Adjust plan",()=>OpenPlanEditor(plan));
            Item("停止计划","Stop plan",()=>SetLearningPlanStopped(plan));
        }
        else if(plan.Status==DailyStudyPlanStatus.Stopped)
            Item("恢复计划","Resume plan",()=>ResumeLearningPlan(plan));
        if(plan.CurrentBatchWordIds.Count>0)
            Item("最近批次拼写练习","Practice latest batch",()=>ShowLastPlanBatch(plan));
        if(plan.Status!=DailyStudyPlanStatus.Active)
            Item("删除计划…","Delete plan…",()=>ConfirmDeletePlan(plan,anchor));
        menu.ShowAt(anchor);
    }

    private void ClosePlanEditor()
    {
        if(_planEditDrawer?.Parent is Panel parent) parent.Children.Remove(_planEditDrawer);
        _planEditDrawer=null;
    }

    private void OpenPlanEditor(DailyStudyPlan plan)
    {
        ClosePlanEditor();
        var main=(Grid)((Grid)RootWindowBorder.Child!).Children[0];
        PlanEditorControl? editor=null;
        editor=new PlanEditorControl(new PlanEditorModel(plan.Name,plan.DailyWordCount,plan.RandomOrder,plan.Words.Count-plan.CompletedWordIds.Count),model=>
        {
            if(_restoring || !_databaseAvailable) {editor!.SetError(UiText.Bilingual("词库暂不可用。","Archive unavailable."));return;}
            var original=_learningPlans;
            AdjustLearningPlan(plan,model.Name,model.DailyCount,model.Shuffle);
            if(!ReferenceEquals(original,_learningPlans)) ClosePlanEditor();
            else editor!.SetError(UiText.Bilingual(
                "计划未能保存，请稍后重试。",
                "The plan could not be saved. Please try again."));
        },ClosePlanEditor);
        editor.Name="PlanEditor";
        _planEditDrawer=new Border {Name="PlanEditorPage",Child=new ScrollViewer {Content=editor,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled},Padding=new Thickness(24,16),IsVisible=true};
        _planEditDrawer.Bind(Border.BackgroundProperty,this.GetResourceObservable("PaperBrush"));
        _planEditDrawer.SetValue(Panel.ZIndexProperty,210);Grid.SetRow(_planEditDrawer,1);main.Children.Add(_planEditDrawer);
    }
    private void ConfirmDeletePlan(DailyStudyPlan plan,Control anchor)
    {
        var body=new StackPanel {Spacing=12};
        body.Children.Add(LearningBilingualText(
            "删除只清除此计划记录，词汇档案不受影响。",
            "Only this plan record is deleted. Your vocabulary archive is not affected."));
        var confirm=new Flyout {Content=body};
        TrackStudyFlyout(confirm);
        body.Children.Add(LearningBilingualButton("确认删除此计划","Delete this plan",()=>
        {
            var updated=_learningPlans.Where(p=>p.Id!=plan.Id).ToList();
            if(!SaveLearningPlans(updated,intentionalPlanChange:true))return;
            _learningPlans=updated;
            if(_learningPlanDetailId==plan.Id) _learningPlanDetailId=null;
            if(_typingReturnPlanId==plan.Id) _typingReturnPlanId=null;
            if(_activeLearningPlan?.Id==plan.Id) {_activeLearningPlan=null;_dailyLearningSession=null;_studyWorkspaceHost.Children.Clear();_studyWorkspaceHost.IsVisible=false;_managementHost.IsVisible=true;}
            confirm.Hide(); RenderLearningPlans();
        }));
        body.Children.Add(LearningBilingualButton("取消","Cancel",()=>confirm.Hide()));
        confirm.ShowAt(anchor);
    }
}
