using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lexi.Features.Learning;

namespace Lexi;

public partial class MainWindow
{
    private sealed record PlanCreatorSectionChoice(
        string Id,
        string Display,
        LearningSection? Section,
        bool WholeBook = false)
    {
        public override string ToString() => Display;
    }

    private sealed record PlanCreatorTopicChoice(string Id, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record PlanCreatorBookChoice(string Id, string Display, ExamBook Book)
    {
        public override string ToString() => Display;
    }

    private sealed record PlanCreatorWordOrigin(
        string TopicId,
        ExamBook Book,
        LearningSection Section);

    private Border? _planCreatorOverlay;
    private ContentControl? _planCreatorContent;
    private Action? _refreshOpenPlanCreatorLanguage;
    private bool _planCreatorLanguageHooked;
    private int _planCreatorLanguageRefreshVersion;
    private string _planCreatorObservedLanguage = "zh-CN";

    private void ClosePlanCreator()
    {
        _refreshOpenPlanCreatorLanguage = null;
        _planCreatorLanguageRefreshVersion++;
        if (_planCreatorOverlay != null)
            ((Grid)((Grid)RootWindowBorder.Child!).Children[0]).Children.Remove(_planCreatorOverlay);
        _planCreatorOverlay = null; _planCreatorContent = null;
    }

    private void EnsurePlanCreatorLanguageHook()
    {
        if (_planCreatorLanguageHooked) return;
        _planCreatorLanguageHooked = true;

        void QueueRefresh()
        {
            var version = ++_planCreatorLanguageRefreshVersion;
            Dispatcher.UIThread.Post(() =>
            {
                if (version != _planCreatorLanguageRefreshVersion ||
                    _refreshOpenPlanCreatorLanguage == null ||
                    _planCreatorObservedLanguage == UiText.Language)
                    return;
                _planCreatorObservedLanguage = UiText.Language;
                _refreshOpenPlanCreatorLanguage();
            });
        }

        LanguageToggleBtn.Click += (_, _) => QueueRefresh();
        SettingsLanguageCombo.SelectionChanged += (_, _) => QueueRefresh();
    }

    private void OpenPlanCreator(
        DailyStudyPlanSource source,
        LearningSection? initialSection = null,
        ExamBook? examBook = null,
        IReadOnlyList<LearningWord>? initialWords = null,
        bool wholeBook = false)
        => OpenPlanCreator(source, examBook?.TopicId, examBook, initialSection, initialWords, wholeBook);

    private void OpenPlanCreator(
        DailyStudyPlanSource source,
        string? topicId,
        ExamBook? examBook,
        LearningSection? initialSection,
        IReadOnlyList<LearningWord>? currentSelection,
        bool wholeBook)
    {
        if (_planCreatorOverlay != null) return;
        EnsurePlanCreatorLanguageHook();
        _planCreatorObservedLanguage = UiText.Language;
        if (_learningPlansLoadFailed || !_databaseAvailable || _restoring)
        {
            SetStatus(UiText.Bilingual(
                "计划数据暂不可用，请先完成词库恢复。",
                "Plan data is unavailable. Finish restoring the library first."));
            return;
        }

        var requestedTopicId = topicId?.Trim();
        if (examBook != null &&
            (string.IsNullOrWhiteSpace(requestedTopicId) ||
             !string.Equals(requestedTopicId, examBook.TopicId?.Trim(), StringComparison.Ordinal)))
        {
            SetStatus(UiText.Bilingual(
                "考试计划范围无效，请重新打开创建入口。",
                "The exam plan scope is invalid. Reopen the plan creator."));
            return;
        }

        bool IsExamTopic(string? id)
        {
            var normalized = id?.Trim();
            return !string.IsNullOrWhiteSpace(normalized) &&
                   (string.Equals(normalized, "ielts", StringComparison.Ordinal) ||
                    ExamCatalog.TopicIds.Contains(normalized, StringComparer.Ordinal));
        }

        var initialTopicId = source == DailyStudyPlanSource.Ielts
            ? examBook?.TopicId?.Trim() ?? requestedTopicId ?? "ielts"
            : "";
        if (source == DailyStudyPlanSource.Ielts && !IsExamTopic(initialTopicId))
        {
            SetStatus(UiText.Bilingual(
                "考试专题标识无效，无法创建计划。",
                "The exam topic identifier is invalid, so the plan cannot be created."));
            return;
        }

        var activeTopicId = initialTopicId;
        var activeBooks = source == DailyStudyPlanSource.Ielts
            ? ExamBooksFor(activeTopicId)
            : [];
        if (examBook != null && activeBooks.All(book => book.Id != examBook.Id))
            activeBooks.Insert(0, examBook);
        var activeBook = examBook == null
            ? activeBooks.FirstOrDefault()
            : activeBooks.FirstOrDefault(book => book.Id == examBook.Id) ?? examBook;
        if (activeBook != null && initialSection != null)
            initialSection = activeBook.Sections.FirstOrDefault(section => section.Id == initialSection.Id);
        var activeWholeBook = source == DailyStudyPlanSource.Ielts && wholeBook;
        var activeSection = activeWholeBook
            ? null
            : initialSection ?? activeBook?.Sections.FirstOrDefault();

        if (source == DailyStudyPlanSource.Ielts &&
            (activeBook == null || (!activeWholeBook && activeSection == null)))
        {
            SetStatus(UiText.Bilingual(
                "当前考试专题没有可用的词书或章节，请稍后重试。",
                "The selected exam topic has no available books or sections. Try again later."));
            return;
        }

        var selected = new HashSet<string>(StringComparer.Ordinal);
        var initializedScopes = new PlanScopeInitializationTracker();
        var selectedBookByTopic = new Dictionary<string, string>(StringComparer.Ordinal);
        var selectedSectionByBook = new Dictionary<string, string>(StringComparer.Ordinal);
        if (activeBook != null)
            selectedBookByTopic[activeTopicId] = activeBook.Id;
        if (activeBook != null)
            selectedSectionByBook[activeBook.Id] = activeWholeBook
                ? ExamAllUnitsId
                : activeSection?.Id ?? ExamAllUnitsId;
        var knownBooks = new Dictionary<string, ExamBook>(StringComparer.Ordinal);
        var knownBookTopics = new Dictionary<string, string>(StringComparer.Ordinal);
        var examWordsById = new Dictionary<string, DailyStudyPlanWord>(StringComparer.Ordinal);
        var wordOrigins = new Dictionary<string, PlanCreatorWordOrigin>(StringComparer.Ordinal);

        void RegisterExamBook(string topicIdForBook, ExamBook book)
        {
            knownBooks[book.Id] = book;
            knownBookTopics[book.Id] = topicIdForBook;
            foreach (var section in book.Sections)
            {
                foreach (var word in section.Entries)
                {
                    examWordsById[word.Id] = ToPlanWord(word);
                    wordOrigins[word.Id] = new PlanCreatorWordOrigin(topicIdForBook, book, section);
                }
            }
        }

        foreach (var book in activeBooks)
            RegisterExamBook(activeTopicId, book);

        // Entries supplied by the entry point may already span books. Register their
        // catalog origins before importing the current selection so book-level IDs survive.
        if (source == DailyStudyPlanSource.Ielts &&
            currentSelection != null &&
            currentSelection.Any(word => ExamCatalog.IsExamWordId(word.Id)))
        {
            var catalog = EnsureExamCatalog();
            if (catalog != null)
            {
                var selectedBooks = currentSelection
                    .Select(word => catalog.FindBookByWordId(word.Id))
                    .Where(book => book != null)
                    .Cast<ExamBook>()
                    .DistinctBy(book => book.Id, StringComparer.Ordinal);
                foreach (var book in selectedBooks)
                    RegisterExamBook(book.TopicId, book);
            }
        }

        void AddSectionWords(ExamBook book, LearningSection section)
        {
            var topicIdForSection = knownBookTopics.GetValueOrDefault(book.Id) ?? activeTopicId;
            foreach (var word in section.Entries)
            {
                selected.Add(word.Id);
                examWordsById[word.Id] = ToPlanWord(word);
                wordOrigins[word.Id] = new PlanCreatorWordOrigin(topicIdForSection, book, section);
            }
        }

        void AddCurrentScopeWords()
        {
            if (activeBook == null) return;
            if (activeWholeBook)
            {
                foreach (var section in activeBook.Sections)
                    AddSectionWords(activeBook, section);
            }
            else if (activeSection != null)
            {
                AddSectionWords(activeBook, activeSection);
            }
        }

        var archiveWords = _allWords.Select(w => new DailyStudyPlanWord
        {
            Id = w.Id.ToString(CultureInfo.InvariantCulture),
            Word = w.Word,
            Meaning = w.Translation,
            Phonetic = w.Phonetic,
            Definition = w.Definition,
            Example = string.Join("\n", w.AiExamples.Select(e => e.English + "\n" + e.Chinese))
        }).ToList();

        var preselectedNotice = "";
        if (source == DailyStudyPlanSource.Archive)
        {
            var marked = _allWords.Where(w => w.Selected)
                .Select(w => w.Id.ToString(CultureInfo.InvariantCulture))
                .ToList();
            if (marked.Count > 0)
            {
                selected.UnionWith(marked);
                preselectedNotice = UiText.Bilingual(
                    $"已预置词汇档案中已勾选的 {marked.Count} 个词条。",
                    $"{marked.Count} entries checked in the vocabulary archive are preselected.");
            }
            else
            {
                // 通用创建默认不全选整个档案
                preselectedNotice = UiText.Bilingual(
                    "通用创建：未默认全选，请按需选择本页或搜索词条。",
                    "General plan: no words are selected by default. Select this page or search for entries.");
            }
        }
        else
        {
            if (currentSelection == null)
                AddCurrentScopeWords();

            if (currentSelection != null)
            {
                var requestedIds = currentSelection
                    .Select(word => word.Id)
                    .Where(examWordsById.ContainsKey)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                selected.UnionWith(requestedIds);
                foreach (var id in requestedIds)
                {
                    if (!wordOrigins.TryGetValue(id, out var origin)) continue;
                    initializedScopes.MarkInitialized(origin.Book.Id, origin.Section.Id, wholeBook: false);
                }

                preselectedNotice = requestedIds.Count > 0
                    ? UiText.Bilingual(
                        $"已按创建入口的当前选择预置 {requestedIds.Count} 个词条；未勾选的部分不会被自动补回。",
                        $"{requestedIds.Count} currently selected entries were imported. Entries you cleared are not added back.")
                    : UiText.Bilingual(
                        "创建入口当前选择为空；将保持空选择，不会自动改为整章全选。",
                        "The creation entry supplied an empty selection. It stays empty and will not fall back to the whole section.");
            }
            else
            {
                if (activeWholeBook && activeBook != null)
                {
                    var bookCount = activeBook.AllWords.Count();
                    preselectedNotice = UiText.Bilingual(
                        $"当前范围为整本词书：已默认选取全部 {bookCount} 个词条。",
                        $"The current scope is the entire book: all {bookCount} entries are selected by default.");
                }
                else
                {
                    var sectionCount = activeSection?.Entries.Count ?? 0;
                    preselectedNotice = UiText.Bilingual(
                        $"首次访问章节「{activeSection?.Title}」：已并入该章节全部 {sectionCount} 个词条。",
                        $"First visit to \"{activeSection?.EnglishTitle}\": all {sectionCount} entries in this section were added.");
                }
            }
        }
        if (source == DailyStudyPlanSource.Ielts && activeBook != null)
            initializedScopes.MarkInitialized(activeBook.Id, activeSection?.Id, activeWholeBook);

        string ExamTopicName(string topicIdForName, bool english) => topicIdForName switch
        {
            "ielts" => "IELTS",
            "cet4" => english ? "CET-4" : "四级",
            "cet6" => english ? "CET-6" : "六级",
            "kaoyan" => english ? "Postgraduate" : "考研",
            "gmat" => "GMAT",
            "toefl" => "TOEFL",
            "gre" => "GRE",
            _ => ""
        };

        string ExamBookName(ExamBook? book, bool english) => book == null
            ? ""
            : english
                ? string.IsNullOrWhiteSpace(book.EnglishTitle) ? book.Title : book.EnglishTitle
                : book.Title;

        string ExamSectionName(LearningSection section, bool english) => english
            ? string.IsNullOrWhiteSpace(section.EnglishTitle) ? section.Title : section.EnglishTitle
            : section.Title;

        string SectionListText(IReadOnlyList<LearningSection> matchedSections, bool english)
        {
            if (matchedSections.Count == 0) return english ? "No section selected" : "未选择章节";
            if (matchedSections.Count == 1) return ExamSectionName(matchedSections[0], english);
            if (matchedSections.Count == 2)
                return string.Join(
                    english ? " + " : "、",
                    matchedSections.Select(section => ExamSectionName(section, english)));
            return english ? $"{matchedSections.Count} sections" : $"{matchedSections.Count} 个章节";
        }

        string ExamScopeText(bool english, IReadOnlyCollection<string> ids)
        {
            var idSet = ids as HashSet<string> ?? ids.ToHashSet(StringComparer.Ordinal);
            var origins = idSet
                .Select(id => wordOrigins.GetValueOrDefault(id))
                .Where(origin => origin != null)
                .Cast<PlanCreatorWordOrigin>()
                .DistinctBy(origin => origin.Book.Id)
                .ToList();

            if (origins.Count == 0 && activeBook != null)
            {
                return $"{ExamTopicName(activeTopicId, english)} · {ExamBookName(activeBook, english)} · " +
                       (activeWholeBook
                           ? english ? "Entire book" : "整本词书"
                           : ExamSectionName(activeSection ?? activeBook.Sections[0], english));
            }

            if (origins.Count == 0)
                return english ? "No exam scope selected" : "未选择考试范围";

            var topics = origins.Select(origin => origin.TopicId).Distinct(StringComparer.Ordinal).ToList();
            if (origins.Count > 1)
            {
                var matchedSections = idSet
                    .Select(id => wordOrigins.GetValueOrDefault(id)?.Section)
                    .Where(section => section != null)
                    .Cast<LearningSection>()
                    .DistinctBy(section => section.Id)
                    .ToList();
                var topicPart = topics.Count == 1
                    ? ExamTopicName(topics[0], english)
                    : english ? $"{topics.Count} exam topics" : $"{topics.Count} 个考试专题";
                var bookPart = english ? $"{origins.Count} books" : $"{origins.Count} 本词书";
                return $"{topicPart} · {bookPart} · {SectionListText(matchedSections, english)}";
            }

            var origin = origins[0];
            var matchedBookSections = origin.Book.Sections
                .Where(section => section.Entries.Any(word => idSet.Contains(word.Id)))
                .ToList();
            var wholeBook = idSet.Count > 0 &&
                            origin.Book.AllWords.All(word => idSet.Contains(word.Id));
            string sectionPart;
            if (wholeBook)
            {
                sectionPart = english ? "Entire book" : "整本词书";
            }
            else
            {
                sectionPart = SectionListText(matchedBookSections, english);
                var completeSelection = matchedBookSections.Count > 0 &&
                                        matchedBookSections.All(section => section.Entries.All(word => idSet.Contains(word.Id)));
                if (idSet.Count > 0 && !completeSelection)
                {
                    sectionPart += english
                        ? $" · {idSet.Count} selected words"
                        : $" · 已选 {idSet.Count} 词";
                }
            }

            return $"{ExamTopicName(origin.TopicId, english)} · " +
                   $"{ExamBookName(origin.Book, english)} · {sectionPart}";
        }

        string DefaultExamPlanName()
        {
            var chinese = ExamScopeText(false, selected);
            var english = ExamScopeText(true, selected);
            return UiText.Bilingual($"我的 {chinese} 计划", $"My {english} Plan");
        }

        List<PlanCreatorTopicChoice> BuildTopicChoices() => ExamUiTopicIds
            .Select(topic => new PlanCreatorTopicChoice(
                topic,
                UiText.Bilingual(
                    ExamTopicName(topic, false),
                    ExamTopicName(topic, true))))
            .ToList();

        List<PlanCreatorBookChoice> BuildBookChoices() => activeBooks
            .Select(book => new PlanCreatorBookChoice(
                book.Id,
                UiText.Bilingual(
                    ExamBookName(book, false),
                    ExamBookName(book, true)),
                book))
            .ToList();

        List<PlanCreatorSectionChoice> BuildSectionChoices(ExamBook? book)
        {
            var choices = new List<PlanCreatorSectionChoice>();
            if (book == null) return choices;
            var bookCount = book.AllWords.Count();
            choices.Add(new PlanCreatorSectionChoice(
                ExamAllUnitsId,
                UiText.Bilingual(
                    $"整本词书 · {bookCount} 词",
                    $"Entire book · {bookCount} words"),
                null,
                WholeBook: true));
            choices.AddRange(book.Sections.Select(section =>
                new PlanCreatorSectionChoice(
                    section.Id,
                    UiText.Bilingual(
                        ExamSectionName(section, false),
                        ExamSectionName(section, true)),
                    section)));
            return choices;
        }

        var dialog = new ContentControl
        {
            Name = "PlanCreatorContent", MaxWidth = 1040,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };

        var rootGrid = new Grid();

        // ==================== Step 1: 选词视图 ====================
        var step1View = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(24, 20),
            RowSpacing = 12
        };

        // Step 1 Header
        var step1Header = new StackPanel { Spacing = 10 };
        var step1TitleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        step1TitleRow.Children.Add(new TextBlock
        {
            Text = UiText.Bilingual("创建每日学习计划", "Create a daily study plan"),
            FontSize = 20,
            FontWeight = FontWeight.SemiBold
        });
        var step1Badge = new TextBlock
        {
            Text = UiText.Bilingual("第 1 步 / 共 2 步 · 选择词条", "Step 1 of 2 · Select entries"),
            FontSize = 13,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(step1Badge, 1);
        step1TitleRow.Children.Add(step1Badge);
        step1Header.Children.Add(step1TitleRow);

        var scopeNoticeText = new TextBlock
        {
            Text = preselectedNotice,
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap
        };
        step1Header.Children.Add(scopeNoticeText);

        ComboBox? topicPicker = null;
        ComboBox? bookPicker = null;
        ComboBox? sectionPicker = null;
        if (source == DailyStudyPlanSource.Ielts)
        {
            var topicChoices = BuildTopicChoices();
            var bookChoices = BuildBookChoices();
            var sectionChoices = BuildSectionChoices(activeBook);

            topicPicker = new ComboBox
            {
                Name = "PlanCreatorTopicPicker",
                ItemsSource = topicChoices,
                SelectedItem = topicChoices.FirstOrDefault(choice => choice.Id == activeTopicId),
                MinWidth = 170
            };
            bookPicker = new ComboBox
            {
                Name = "PlanCreatorBookPicker",
                ItemsSource = bookChoices,
                SelectedItem = bookChoices.FirstOrDefault(choice => choice.Id == activeBook?.Id),
                MinWidth = 250
            };
            sectionPicker = new ComboBox
            {
                Name = "PlanCreatorSectionPicker",
                ItemsSource = sectionChoices,
                SelectedItem = sectionChoices.FirstOrDefault(choice =>
                    choice.Id == (activeWholeBook ? ExamAllUnitsId : activeSection?.Id)),
                MinWidth = 280,
            };

            Control ScopeField(string labelChinese, string labelEnglish, ComboBox picker)
            {
                var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 12, 0) };
                field.Children.Add(new TextBlock
                {
                    Text = UiText.Bilingual(labelChinese, labelEnglish),
                    FontSize = 12,
                    Opacity = 0.72
                });
                field.Children.Add(picker);
                return field;
            }

            var scopePanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            scopePanel.Children.Add(ScopeField("考试专题", "Exam topic", topicPicker));
            scopePanel.Children.Add(ScopeField("词书", "Book", bookPicker));
            scopePanel.Children.Add(ScopeField("章节", "Section", sectionPicker));
            step1Header.Children.Add(scopePanel);
        }

        var searchBox = new TextBox
        {
            Name = "PlanCreatorSearch",
            Watermark = UiText.Bilingual(
                "搜索词条（按英文单词或中文释义）",
                "Search entries by English word or meaning")
        };
        step1Header.Children.Add(searchBox);

        var selectionToolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        var btnSelectPage = LearningButton(UiText.Bilingual("选本页", "Select page"), () => { });
        var btnSelectFiltered = LearningButton(UiText.Bilingual("选当前筛选结果", "Select filtered results"), () => { });
        var btnClearFiltered = LearningButton(UiText.Bilingual("清空当前筛选", "Clear filtered results"), () => { });
        var btnClearAll = LearningButton(UiText.Bilingual("清空全部选择", "Clear all selections"), () => { });
        var chkOnlySelected = new CheckBox
        {
            Name = "PlanCreatorOnlySelected",
            Content = UiText.Bilingual("仅看已选", "Selected only"),
            IsChecked = false,
            Margin = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        selectionToolbar.Children.Add(btnSelectPage);
        selectionToolbar.Children.Add(chkOnlySelected);
        var moreActions = new StackPanel { Spacing = 8 };
        moreActions.Children.Add(btnSelectFiltered); moreActions.Children.Add(btnClearFiltered); moreActions.Children.Add(btnClearAll);
        var moreFlyout = new Flyout { Content = moreActions };
        var moreButton = LearningButton(UiText.Bilingual("更多…", "More…"), () => { });
        moreButton.Flyout = moreFlyout;
        foreach (var action in moreActions.Children.OfType<Button>()) action.Click += (_, _) => moreFlyout.Hide();
        selectionToolbar.Children.Add(moreButton);
        step1Header.Children.Add(selectionToolbar);

        Grid.SetRow(step1Header, 0);
        step1View.Children.Add(step1Header);

        // Step 1 Middle Word List (Bounded * row)
        var listContainer = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 6
        };

        var listHeaderBar = new Border
        {
            Padding = new Thickness(8, 4),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = this.FindResource("LineBrush") as IBrush ?? Brushes.LightGray
        };
        var listHeaderCols = new Grid { ColumnDefinitions = new ColumnDefinitions("44,180,*") };
        listHeaderCols.Children.Add(new TextBlock
        {
            Text = UiText.Bilingual("选择", "Select"),
            FontSize = 12,
            Opacity = 0.6
        });
        var colWord = new TextBlock
        {
            Text = UiText.Bilingual("单词 · 音标", "Word · Phonetic"),
            FontSize = 12,
            Opacity = 0.6
        };
        Grid.SetColumn(colWord, 1);
        listHeaderCols.Children.Add(colWord);
        var colMeaning = new TextBlock
        {
            Text = UiText.Bilingual("中文释义与例句", "Meaning & examples"),
            FontSize = 12,
            Opacity = 0.6
        };
        Grid.SetColumn(colMeaning, 2);
        listHeaderCols.Children.Add(colMeaning);
        listHeaderBar.Child = listHeaderCols;
        Grid.SetRow(listHeaderBar, 0);
        listContainer.Children.Add(listHeaderBar);

        var rowsPanel = new StackPanel { Spacing = 4 };
        var listScroll = new ScrollViewer
        {
            Content = rowsPanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden
        };
        Grid.SetRow(listScroll, 1);
        listContainer.Children.Add(listScroll);

        var pagerBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var btnPrevPage = LearningButton(UiText.Bilingual("上一页", "Previous"), () => { });
        var pageIndicator = new TextBlock
        {
            Text = UiText.Bilingual("第 1 / 1 页", "Page 1 of 1"),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13
        };
        var btnNextPage = LearningButton(UiText.Bilingual("下一页", "Next"), () => { });
        pagerBar.Children.Add(btnPrevPage);
        pagerBar.Children.Add(pageIndicator);
        pagerBar.Children.Add(btnNextPage);
        Grid.SetRow(pagerBar, 2);
        listContainer.Children.Add(pagerBar);

        Grid.SetRow(listContainer, 1);
        step1View.Children.Add(listContainer);

        // Step 1 Footer
        var step1Footer = new Border
        {
            Padding = new Thickness(0, 10, 0, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = this.FindResource("LineBrush") as IBrush ?? Brushes.LightGray
        };
        var footerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var selectedCountText = new TextBlock
        {
            Text = UiText.Bilingual("已选 0 词", "0 words selected"),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            FontWeight = FontWeight.Medium
        };
        footerGrid.Children.Add(selectedCountText);

        var step1Buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var btnCancel1 = LearningButton(UiText.Bilingual("取消", "Cancel"), ClosePlanCreator);
        var btnNextToStep2 = LearningButton(
            UiText.Bilingual("下一步：安排计划 →", "Next: Schedule plan →"),
            () => { },
            primary: true);
        step1Buttons.Children.Add(btnCancel1);
        step1Buttons.Children.Add(btnNextToStep2);
        Grid.SetColumn(step1Buttons, 1);
        footerGrid.Children.Add(step1Buttons);
        step1Footer.Child = footerGrid;

        Grid.SetRow(step1Footer, 2);
        step1View.Children.Add(step1Footer);

        // ==================== Step 2: 安排视图 ====================
        var step2View = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(24, 20),
            RowSpacing = 14,
            IsVisible = false
        };

        // Step 2 Header
        var step2Header = new StackPanel { Spacing = 10 };
        var step2TitleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        step2TitleRow.Children.Add(new TextBlock
        {
            Text = UiText.Bilingual("创建每日学习计划", "Create a daily study plan"),
            FontSize = 20,
            FontWeight = FontWeight.SemiBold
        });
        var step2Badge = new TextBlock
        {
            Text = UiText.Bilingual("第 2 步 / 共 2 步 · 安排计划", "Step 2 of 2 · Schedule plan"),
            FontSize = 13,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(step2Badge, 1);
        step2TitleRow.Children.Add(step2Badge);
        step2Header.Children.Add(step2TitleRow);

        var step2SummaryCard = new Border
        {
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent
        };
        step2SummaryCard.Background=Brushes.Transparent;
        var step2SummaryText = new TextBlock
        {
            Text = "",
            FontSize = 13,
            FontWeight = FontWeight.Medium
        };
        step2SummaryCard.Child = step2SummaryText;
        step2Header.Children.Add(step2SummaryCard);

        Grid.SetRow(step2Header, 0);
        step2View.Children.Add(step2Header);

        // Step 2 Form Body (ScrollViewer bounded)
        var step2FormScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden
        };
        var step2Form = new StackPanel { Spacing = 14, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Stretch };

        var defaultPlanName = source == DailyStudyPlanSource.Ielts
            ? DefaultExamPlanName()
            : UiText.Bilingual("我的词汇计划", "My Vocabulary Plan");
        var planEditor=new PlanEditorControl(new PlanEditorModel(defaultPlanName,20,false,selected.Count),_=>{},()=>{});
        planEditor.FutureBatchNoticeBlock.IsVisible=false;
        planEditor.SaveButton.IsVisible=false;planEditor.CancelButton.IsVisible=false;
        var planNameInput=planEditor.NameInput;planNameInput.Name="PlanCreatorName";
        var quotaInput=planEditor.DailyQuotaInput;quotaInput.Name="PlanCreatorQuota";
        var randomCheckbox=planEditor.ShuffleCheckbox;randomCheckbox.Name="PlanCreatorRandom";
        var estimateBody=planEditor.SummaryBlock;
        var lastGeneratedPlanName = defaultPlanName;
        step2Form.Children.Add(planEditor);
        // Conflict Warning Panel (Explicit Confirmation Area)
        var conflictPanel = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Orange,
            Background = Brushes.Transparent,
            IsVisible = false
        };
        conflictPanel.Bind(Border.BackgroundProperty, this.GetResourceObservable("CardBrush"));
        var conflictLayout = new StackPanel { Spacing = 10 };
        var conflictTitle = new TextBlock
        {
            Text = UiText.Bilingual(
                "检测到与其他进行中计划存在重复词条",
                "These entries overlap with another active plan"),
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.DarkOrange
        };
        var conflictDetails = new TextBlock
        {
            Text = "",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        };
        var conflictExplanation = new TextBlock
        {
            Text = UiText.Bilingual(
                "说明：每个计划的学习进度独立记录。若确认在不同计划中同时学习这些词条，请点击下方确认继续；亦可返回上一步调整选词。",
                "Each plan keeps independent progress. If you want to study these entries in multiple plans, confirm below; otherwise go back and adjust the selection."),
            FontSize = 12,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap
        };
        var conflictActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        var btnBackToStep1FromConflict = LearningButton(
            UiText.Bilingual("返回修改选词", "Back to selection"),
            () => { });
        var btnConfirmConflictContinue = LearningButton(
            UiText.Bilingual("我已知晓，仍然创建计划", "I understand; create anyway"),
            () => { },
            primary: true);
        conflictActions.Children.Add(btnBackToStep1FromConflict);
        conflictActions.Children.Add(btnConfirmConflictContinue);

        conflictLayout.Children.Add(conflictTitle);
        conflictLayout.Children.Add(conflictDetails);
        conflictLayout.Children.Add(conflictExplanation);
        conflictLayout.Children.Add(conflictActions);
        conflictPanel.Child = conflictLayout;
        step2Form.Children.Add(conflictPanel);

        var formErrorText = new TextBlock
        {
            Text = "",
            FontSize = 13,
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap
        };
        step2Form.Children.Add(formErrorText);

        step2FormScroll.Content = step2Form;
        Grid.SetRow(step2FormScroll, 1);
        step2View.Children.Add(step2FormScroll);

        // Step 2 Footer
        var step2Footer = new Border
        {
            Padding = new Thickness(0, 10, 0, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = this.FindResource("LineBrush") as IBrush ?? Brushes.LightGray
        };
        var footer2Grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var btnBackToStep1 = LearningButton(
            UiText.Bilingual("← 上一步：重新选词", "← Back: Edit selection"),
            () => { });
        btnBackToStep1.HorizontalAlignment=HorizontalAlignment.Left;
        footer2Grid.Children.Add(btnBackToStep1);

        var step2Buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var btnCancel2 = LearningButton(UiText.Bilingual("取消", "Cancel"), ClosePlanCreator);
        var btnSubmitPlan = LearningButton(
            UiText.Bilingual("创建计划", "Create plan"),
            () => { },
            primary: true);
        step2Buttons.Children.Add(btnCancel2);
        step2Buttons.Children.Add(btnSubmitPlan);
        Grid.SetColumn(step2Buttons, 1);
        footer2Grid.Children.Add(step2Buttons);
        step2Footer.Child = footer2Grid;

        Grid.SetRow(step2Footer, 2);
        step2View.Children.Add(step2Footer);

        rootGrid.Children.Add(step1View);
        rootGrid.Children.Add(step2View);
        dialog.Content = rootGrid;

        // ==================== 数据筛选与联动逻辑 ====================
        var page = 0;
        const int pageSize = PlanCreationState.PageSize;
        var isSubmitting = false;

        IReadOnlyList<DailyStudyPlanWord> CurrentCatalogWords()
        {
            if (source == DailyStudyPlanSource.Archive) return archiveWords;
            if (activeWholeBook) return activeBook?.AllWords.Select(ToPlanWord).ToList() ?? [];
            return activeSection?.Entries.Select(ToPlanWord).ToList() ?? [];
        }

        List<DailyStudyPlanWord> FilteredWords()
        {
            var baseList = CurrentCatalogWords();
            var keyword = searchBox.Text?.Trim();
            var query = baseList.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(w =>
                    w.Word.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    w.Meaning.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    w.Definition.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            if (chkOnlySelected.IsChecked == true)
            {
                query = query.Where(w => selected.Contains(w.Id));
            }

            return query.ToList();
        }

        List<DailyStudyPlanWord> SelectedWords()
        {
            if (source == DailyStudyPlanSource.Archive)
                return archiveWords.Where(w => selected.Contains(w.Id)).DistinctBy(w => w.Id).ToList();

            return selected
                .Select(id => examWordsById.GetValueOrDefault(id))
                .Where(word => word != null)
                .Cast<DailyStudyPlanWord>()
                .DistinctBy(word => word.Id)
                .ToList();
        }

        string PlanSourceLabel()
        {
            if (source == DailyStudyPlanSource.Archive)
                return UiText.Bilingual("词汇档案", "Vocabulary archive");

            var ids = selected.ToArray();
            return UiText.Bilingual(
                ExamScopeText(false, ids),
                ExamScopeText(true, ids));
        }

        string EstimateText(int count, int quota)
        {
            if (count <= 0)
            {
                return UiText.Bilingual(
                    "请至少选择 1 个词条。",
                    "Select at least one entry.");
            }

            if (quota <= 0)
            {
                return UiText.Bilingual(
                    "每日词数需大于 0。",
                    "The daily target must be greater than zero.");
            }

            var days = (count + quota - 1) / quota;
            if (quota >= count)
            {
                return UiText.Bilingual(
                    $"每日目标超过或等于选词总数，预计 1 天完成（首日学习全部 {count} 词）。",
                    $"The daily target covers all selected words; expected completion in 1 day ({count} words).");
            }

            var remainder = count % quota;
            if (remainder == 0)
            {
                return UiText.Bilingual(
                    $"预计 {days} 天完成（每天学习 {quota} 词）。",
                    $"Expected completion in {days} days ({quota} words per day).");
            }

            return UiText.Bilingual(
                $"预计 {days} 天完成（前 {days - 1} 天每天 {quota} 词，最后 1 天学习剩余 {remainder} 词）。",
                $"Expected completion in {days} days ({quota} words per day, with {remainder} words on the final day).");
        }

        void RefreshAutoPlanName()
        {
            if (source != DailyStudyPlanSource.Ielts) return;
            var current = planNameInput.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(current) &&
                !string.Equals(current, lastGeneratedPlanName, StringComparison.Ordinal))
                return;

            var nextDefault = DefaultExamPlanName();
            planNameInput.Text = nextDefault;
            lastGeneratedPlanName = nextDefault;
        }

        void RefreshStep1Summary()
        {
            var count = selected.Count;
            selectedCountText.Text = UiText.Bilingual(
                $"已选 {count} 词（跨章节与搜索保留）",
                $"{count} words selected (kept across sections and searches)");
            btnNextToStep2.IsEnabled = count > 0;
            RefreshAutoPlanName();
        }

        void RefreshStep2Summary()
        {
            var words = SelectedWords();
            var count = words.Count;
            var quota = (int)(quotaInput.Value ?? 20);
            var sourceChinese = source == DailyStudyPlanSource.Archive
                ? "词汇档案"
                : ExamScopeText(false, words.Select(word => word.Id).ToArray());
            var sourceEnglish = source == DailyStudyPlanSource.Archive
                ? "Vocabulary archive"
                : ExamScopeText(true, words.Select(word => word.Id).ToArray());

            step2SummaryText.Text = UiText.Bilingual(
                $"{sourceChinese} · 已选 {count} 个词条",
                $"{sourceEnglish} · {count} entries selected");
            estimateBody.Text = EstimateText(count, quota);
        }

        Control CreatePlanWordRow(DailyStudyPlanWord word)
        {
            var rowBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8),
                Background = Brushes.Transparent
            };
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("40,180,*") };

            var selectBox = new CheckBox
            {
                IsChecked = selected.Contains(word.Id),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(selectBox, 0);
            rowGrid.Children.Add(selectBox);

            var wordStack = new StackPanel
            {
                Spacing = 2,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var wordText = new SelectableTextBlock
            {
                Text = word.Word,
                FontWeight = FontWeight.SemiBold,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap
            };
            wordText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InkBrush"));
            wordStack.Children.Add(wordText);
            if (!string.IsNullOrWhiteSpace(word.Phonetic))
            {
                var phoneticText = new TextBlock
                {
                    Text = $"/{word.Phonetic.Trim('/')}/",
                    FontSize = 12,
                    Opacity = 0.65,
                    TextWrapping = TextWrapping.Wrap
                };
                phoneticText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
                wordStack.Children.Add(phoneticText);
            }
            Grid.SetColumn(wordStack, 1);
            rowGrid.Children.Add(wordStack);

            var meaningStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            var meaningHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var meaningText = new SelectableTextBlock
            {
                Text = word.Meaning,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                VerticalAlignment = VerticalAlignment.Center
            };
            meaningText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InkBrush"));
            Grid.SetColumn(meaningText, 0);
            meaningHeader.Children.Add(meaningText);

            var detailsPanel = new StackPanel
            {
                Spacing = 4,
                Margin = new Thickness(0, 4, 0, 0),
                IsVisible = false
            };
            var hasDetails = false;
            if (!string.IsNullOrWhiteSpace(word.Definition))
            {
                hasDetails = true;
                var definitionText = new SelectableTextBlock
                {
                    Text = word.Definition,
                    FontSize = 13,
                    Opacity = 0.85,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18
                };
                definitionText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
                detailsPanel.Children.Add(definitionText);
            }
            if (!string.IsNullOrWhiteSpace(word.Example))
            {
                hasDetails = true;
                var exampleText = new SelectableTextBlock
                {
                    Text = word.Example,
                    FontSize = 12,
                    Opacity = 0.8,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18
                };
                exampleText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
                detailsPanel.Children.Add(exampleText);
            }

            if (hasDetails)
            {
                var detailsButton = new Button
                {
                    Classes = { "ghost" },
                    Padding = new Thickness(6, 2),
                    FontSize = 11,
                    Content = UiText.Bilingual("显示详细 ▾", "Show details ▾"),
                    IsVisible = true,
                    VerticalAlignment = VerticalAlignment.Top
                };
                detailsButton.Click += (_, _) =>
                {
                    detailsPanel.IsVisible = !detailsPanel.IsVisible;
                    detailsButton.Content = detailsPanel.IsVisible
                        ? UiText.Bilingual("收起详细 ▴", "Hide details ▴")
                        : UiText.Bilingual("显示详细 ▾", "Show details ▾");
                };
                Grid.SetColumn(detailsButton, 1);
                meaningHeader.Children.Add(detailsButton);
                meaningStack.Children.Add(meaningHeader);
                meaningStack.Children.Add(detailsPanel);
            }
            else
            {
                meaningStack.Children.Add(meaningHeader);
            }
            Grid.SetColumn(meaningStack, 2);
            rowGrid.Children.Add(meaningStack);

            void UpdateSelectionVisual(bool isSelected)
            {
                rowBorder.Background = isSelected
                    ? this.FindResource("SelectionBrush") as IBrush ?? Brushes.Transparent
                    : Brushes.Transparent;
            }

            selectBox.IsCheckedChanged += (_, _) =>
            {
                MarkActiveScopeInitialized();
                var isSelected = selectBox.IsChecked == true;
                if (isSelected) selected.Add(word.Id);
                else selected.Remove(word.Id);
                UpdateSelectionVisual(isSelected);
                conflictPanel.IsVisible = false;
                formErrorText.Text = "";
                RefreshStep1Summary();
            };
            UpdateSelectionVisual(selectBox.IsChecked == true);
            rowBorder.Child = rowGrid;
            return rowBorder;
        }

        void RenderStep1Page()
        {
            rowsPanel.Children.Clear();
            var filtered = FilteredWords();
            var pages = Math.Max(1, (filtered.Count + pageSize - 1) / pageSize);
            page = Math.Clamp(page, 0, pages - 1);

            var pageWords = filtered.Skip(page * pageSize).Take(pageSize).ToList();
            foreach (var word in pageWords)
                rowsPanel.Children.Add(CreatePlanWordRow(word));

            if (filtered.Count == 0)
            {
                rowsPanel.Children.Add(new TextBlock
                {
                    Text = chkOnlySelected.IsChecked == true
                        ? UiText.Bilingual("当前专题暂无已选词条。", "No selected entries in this topic.")
                        : UiText.Bilingual("没有匹配的词条。", "No matching entries."),
                    Opacity = 0.6,
                    Margin = new Thickness(12)
                });
            }

            pageIndicator.Text = UiText.Bilingual(
                $"第 {page + 1} / {pages} 页（当前范围共 {filtered.Count} 词）",
                $"Page {page + 1} of {pages} ({filtered.Count} words in scope)");
            btnPrevPage.IsEnabled = page > 0;
            btnNextPage.IsEnabled = page < pages - 1;
            RefreshStep1Summary();
        }

        // ==================== 事件交互绑定 ====================

        // Step 1 批量选择操作
        btnSelectPage.Click += (_, _) =>
        {
            MarkActiveScopeInitialized();
            var filtered = FilteredWords();
            var pageWords = filtered.Skip(page * pageSize).Take(pageSize).ToList();
            selected.UnionWith(pageWords.Select(w => w.Id));
            conflictPanel.IsVisible = false;
            formErrorText.Text = "";
            RenderStep1Page();
        };

        btnSelectFiltered.Click += (_, _) =>
        {
            MarkActiveScopeInitialized();
            var filtered = FilteredWords();
            selected.UnionWith(filtered.Select(w => w.Id));
            conflictPanel.IsVisible = false;
            formErrorText.Text = "";
            RenderStep1Page();
        };

        btnClearFiltered.Click += (_, _) =>
        {
            MarkActiveScopeInitialized();
            var filtered = FilteredWords();
            selected.ExceptWith(filtered.Select(w => w.Id));
            conflictPanel.IsVisible = false;
            formErrorText.Text = "";
            RenderStep1Page();
        };

        btnClearAll.Click += (_, _) =>
        {
            MarkActiveScopeInitialized();
            selected.Clear();
            conflictPanel.IsVisible = false;
            formErrorText.Text = "";
            RenderStep1Page();
        };

        chkOnlySelected.IsCheckedChanged += (_, _) =>
        {
            page = 0;
            RenderStep1Page();
        };

        btnPrevPage.Click += (_, _) => { page--; RenderStep1Page(); };
        btnNextPage.Click += (_, _) => { page++; RenderStep1Page(); };

        searchBox.TextChanged += (_, _) => { page = 0; RenderStep1Page(); };

        var isUpdatingScope = false;

        void MarkActiveScopeInitialized()
        {
            if (activeBook == null) return;
            initializedScopes.MarkInitialized(activeBook.Id, activeSection?.Id, activeWholeBook);
        }

        void VisitActiveSection(bool fromUserNavigation)
        {
            if (activeBook == null) return;
            selectedBookByTopic[activeTopicId] = activeBook.Id;
            var scopeId = activeWholeBook ? ExamAllUnitsId : activeSection?.Id;
            if (string.IsNullOrWhiteSpace(scopeId)) return;
            selectedSectionByBook[activeBook.Id] = scopeId;
            if (initializedScopes.ShouldSelectEntireScope(activeBook.Id, activeSection?.Id, activeWholeBook))
            {
                if (activeWholeBook)
                {
                    AddCurrentScopeWords();
                    var bookCount = activeBook.AllWords.Count();
                    scopeNoticeText.Text = UiText.Bilingual(
                        $"已切换到整本词书：纳入全部 {bookCount} 个词条。",
                        $"Switched to the entire book: all {bookCount} entries are included.");
                }
                else if (activeSection != null)
                {
                    AddCurrentScopeWords();
                    scopeNoticeText.Text = UiText.Bilingual(
                        $"已切换到章节「{ExamSectionName(activeSection, false)}」：默认选中该章节全部 {activeSection.Entries.Count} 个词条。",
                        $"Switched to \"{ExamSectionName(activeSection, true)}\": all {activeSection.Entries.Count} entries in this section are selected by default.");
                }
            }
            else if (fromUserNavigation)
            {
                scopeNoticeText.Text = activeWholeBook
                    ? UiText.Bilingual(
                        "已切换到整本词书；其他词书与章节的已选词条保持不变。",
                        "Switched to the entire book; selections in other books and sections are unchanged.")
                    : activeSection == null
                        ? UiText.Bilingual("未选择章节。", "No section selected.")
                        : UiText.Bilingual(
                            $"已切换到章节「{ExamSectionName(activeSection, false)}」；其他章节的已选词条保持不变。",
                            $"Switched to \"{ExamSectionName(activeSection, true)}\"; selections in other sections are unchanged.");
            }
        }

        void RefreshSectionScope()
        {
            if (sectionPicker == null) return;
            var choices = BuildSectionChoices(activeBook);
            var rememberedSectionId = activeBook != null
                ? selectedSectionByBook.GetValueOrDefault(activeBook.Id)
                : null;
            var sectionChoice = choices.FirstOrDefault(choice => choice.Id == rememberedSectionId)
                                ?? choices.FirstOrDefault(choice => !choice.WholeBook)
                                ?? choices.FirstOrDefault();
            sectionPicker.ItemsSource = choices;
            sectionPicker.SelectedItem = sectionChoice;
            activeWholeBook = sectionChoice?.WholeBook == true;
            activeSection = sectionChoice?.Section;
            VisitActiveSection(fromUserNavigation: false);
        }

        void RefreshBookScope(string? preferredBookId = null)
        {
            if (bookPicker == null) return;
            var choices = BuildBookChoices();
            var bookChoice = choices.FirstOrDefault(choice => choice.Id == preferredBookId)
                             ?? choices.FirstOrDefault(choice =>
                                 choice.Id == selectedBookByTopic.GetValueOrDefault(activeTopicId))
                             ?? choices.FirstOrDefault();
            bookPicker.ItemsSource = choices;
            bookPicker.SelectedItem = bookChoice;
            activeBook = bookChoice?.Book;
            if (activeBook != null)
                selectedBookByTopic[activeTopicId] = activeBook.Id;
            RefreshSectionScope();
        }

        if (topicPicker != null && bookPicker != null && sectionPicker != null)
        {
            topicPicker.SelectionChanged += (_, _) =>
            {
                if (isUpdatingScope ||
                    topicPicker.SelectedItem is not PlanCreatorTopicChoice topicChoice)
                    return;

                isUpdatingScope = true;
                try
                {
                    activeTopicId = topicChoice.Id;
                    activeBooks = ExamBooksFor(activeTopicId);
                    foreach (var book in activeBooks)
                        RegisterExamBook(activeTopicId, book);
                    activeBook = null;
                    activeSection = null;
                    activeWholeBook = false;
                    RefreshBookScope();
                }
                finally
                {
                    isUpdatingScope = false;
                }

                page = 0;
                RenderStep1Page();
            };

            bookPicker.SelectionChanged += (_, _) =>
            {
                if (isUpdatingScope ||
                    bookPicker.SelectedItem is not PlanCreatorBookChoice bookChoice)
                    return;

                isUpdatingScope = true;
                try
                {
                    activeBook = bookChoice.Book;
                    selectedBookByTopic[activeTopicId] = activeBook.Id;
                    RefreshSectionScope();
                }
                finally
                {
                    isUpdatingScope = false;
                }

                page = 0;
                RenderStep1Page();
            };

            sectionPicker.SelectionChanged += (_, _) =>
            {
                if (isUpdatingScope ||
                    sectionPicker.SelectedItem is not PlanCreatorSectionChoice sectionChoice)
                    return;

                activeWholeBook = sectionChoice.WholeBook;
                activeSection = sectionChoice.Section;
                VisitActiveSection(fromUserNavigation: true);
                page = 0;
                RenderStep1Page();
            };
        }

        void RefreshPlanCreatorLanguage()
        {
            var previousUpdatingScope = isUpdatingScope;
            isUpdatingScope = true;
            try
            {
                if (topicPicker != null && bookPicker != null && sectionPicker != null)
                {
                    var topicChoices = BuildTopicChoices();
                    topicPicker.ItemsSource = topicChoices;
                    topicPicker.SelectedItem = topicChoices.FirstOrDefault(choice => choice.Id == activeTopicId);

                    var bookChoices = BuildBookChoices();
                    bookPicker.ItemsSource = bookChoices;
                    bookPicker.SelectedItem = bookChoices.FirstOrDefault(choice => choice.Id == activeBook?.Id);

                    var sectionChoices = BuildSectionChoices(activeBook);
                    sectionPicker.ItemsSource = sectionChoices;
                    var scopeId = activeWholeBook ? ExamAllUnitsId : activeSection?.Id;
                    sectionPicker.SelectedItem = sectionChoices.FirstOrDefault(choice => choice.Id == scopeId);
                }
            }
            finally
            {
                isUpdatingScope = previousUpdatingScope;
            }

            page = 0;
            RenderStep1Page();
            RefreshStep2Summary();
        }

        // Step 1 -> Step 2 切换
        btnNextToStep2.Click += (_, _) =>
        {
            if (selected.Count == 0)
            {
                SetStatus(UiText.Bilingual(
                    "请至少选择 1 个词条后再安排计划。",
                    "Select at least one entry before scheduling the plan."));
                return;
            }
            step1View.IsVisible = false;
            step2View.IsVisible = true;
            RefreshStep2Summary();
        };

        // Step 2 -> Step 1 切换（保留全部已选与输入）
        void SwitchBackToStep1()
        {
            conflictPanel.IsVisible = false;
            formErrorText.Text = "";
            step2View.IsVisible = false;
            step1View.IsVisible = true;
            RenderStep1Page();
        }

        btnBackToStep1.Click += (_, _) => SwitchBackToStep1();
        btnBackToStep1FromConflict.Click += (_, _) => SwitchBackToStep1();

        quotaInput.ValueChanged += (_, _) => RefreshStep2Summary();

        // 提交创建执行函数
        void ExecuteCreation(DailyStudyPlan planToCreate, int wordCount)
        {
            if (isSubmitting) return;
            isSubmitting = true;
            btnSubmitPlan.IsEnabled = false;
            btnConfirmConflictContinue.IsEnabled = false;

            try
            {
                var updated = _learningPlans.Append(planToCreate).ToList();
                if (!SaveLearningPlans(updated, intentionalPlanChange: true))
                {
                    formErrorText.Text = UiText.Bilingual(
                        "保存失败，请检查计划文件权限或磁盘状态。",
                        "Save failed. Check plan file permissions and disk status.");
                    isSubmitting = false;
                    btnSubmitPlan.IsEnabled = true;
                    btnConfirmConflictContinue.IsEnabled = true;
                    return;
                }

                _learningPlans = updated;
                RenderLearningPlans();
                SetStatus(UiText.Bilingual(
                    $"计划「{planToCreate.Name}」已成功创建，共 {wordCount} 词。",
                    $"Plan \"{planToCreate.Name}\" was created with {wordCount} words."));
                ClosePlanCreator();
            }
            catch (Exception ex)
            {
                formErrorText.Text = UiText.Bilingual(
                    "创建计划出错：",
                    "Could not create the plan: ") + ex.Message;
                isSubmitting = false;
                btnSubmitPlan.IsEnabled = true;
                btnConfirmConflictContinue.IsEnabled = true;
            }
        }

        // Step 2 提交校验与冲突处理
        btnSubmitPlan.Click += (_, _) =>
        {
            formErrorText.Text = "";
            var words = SelectedWords();
            if (words.Count == 0)
            {
                formErrorText.Text = UiText.Bilingual(
                    "已选词数必须大于 0，请返回上一步选择词条。",
                    "Select at least one entry before creating the plan.");
                return;
            }

            var planName = planNameInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(planName))
            {
                formErrorText.Text = UiText.Bilingual(
                    "请输入计划名称。",
                    "Enter a plan name.");
                return;
            }

            var dailyCount = (int)(quotaInput.Value ?? 0);
            if (dailyCount is < 1 or > 10000)
            {
                formErrorText.Text = UiText.Bilingual(
                    "每日词数应在 1–10000 之间。",
                    "The daily target must be between 1 and 10,000.");
                return;
            }

            var sourceLabel = PlanSourceLabel();
            var plan = DailyStudyPlanRules.Create(
                planName,
                source,
                sourceLabel,
                words,
                dailyCount,
                randomCheckbox.IsChecked == true,
                Random.Shared.Next());

            IReadOnlyList<(string PlanName, string Word)> overlaps = source == DailyStudyPlanSource.Ielts
                ? FindExamPlanOverlaps(_learningPlans, plan)
                : DailyStudyPlanRules.FindOverlaps(_learningPlans, plan);
            if (overlaps.Count > 0)
            {
                // 冲突明确展示，并要求用户点击专用的「我已知晓，仍然创建计划」按钮，杜绝同按钮二次隐式点击
                var grouped = overlaps.GroupBy(o => o.PlanName).ToList();
                var summaryLines = grouped.Select(g =>
                {
                    var sampleWords = g.Select(x => x.Word).Distinct().Take(4).ToList();
                    var totalOverlap = g.Select(x => x.Word).Distinct().Count();
                    return UiText.Bilingual(
                        $"• 计划《{g.Key}》：重叠 {totalOverlap} 词（{string.Join("、", sampleWords)}{(totalOverlap > 4 ? " 等" : "")}）",
                        $"• Plan \"{g.Key}\": {totalOverlap} overlapping words ({string.Join(", ", sampleWords)}{(totalOverlap > 4 ? ", etc." : "")})");
                });

                var overlapCount = overlaps.Select(o => o.Word).Distinct().Count();
                conflictDetails.Text = UiText.Bilingual(
                    $"当前选词中有 {overlapCount} 个词正在以下计划中学习：\n",
                    $"{overlapCount} selected words are already studied in these plans:\n") +
                    string.Join("\n", summaryLines);
                conflictPanel.IsVisible = true;
                formErrorText.Text = UiText.Bilingual(
                    "检测到重复词条，请在上方核对并确认是否继续创建。",
                    "Overlapping entries were found. Review the warning above and confirm whether to continue.");
                return;
            }

            ExecuteCreation(plan, words.Count);
        };

        // 冲突明确确认按钮
        btnConfirmConflictContinue.Click += (_, _) =>
        {
            var words = SelectedWords();
            var planName = planNameInput.Text?.Trim() ?? defaultPlanName;
            var dailyCount = (int)(quotaInput.Value ?? 20);
            var sourceLabel = PlanSourceLabel();

            var plan = DailyStudyPlanRules.Create(
                planName,
                source,
                sourceLabel,
                words,
                dailyCount,
                randomCheckbox.IsChecked == true,
                Random.Shared.Next());

            ExecuteCreation(plan, words.Count);
        };

        _refreshOpenPlanCreatorLanguage = RefreshPlanCreatorLanguage;
        RenderStep1Page();
        _planCreatorContent = dialog;
        _planCreatorOverlay = new Border { Child = dialog };
        _planCreatorOverlay.Bind(Border.BackgroundProperty, this.GetResourceObservable("PaperBrush"));
        Grid.SetRow(_planCreatorOverlay, 1); _planCreatorOverlay.SetValue(Panel.ZIndexProperty, 230);
        ((Grid)((Grid)RootWindowBorder.Child!).Children[0]).Children.Add(_planCreatorOverlay);
        searchBox.Focus();
    }

    private static DailyStudyPlanWord ToPlanWord(LearningWord word) => new()
    {
        Id = word.Id,
        Word = word.Word,
        Meaning = word.Meaning,
        Phonetic = word.Phonetic,
        Definition = word.Extra,
        Example = word.Example,
        AudioPath = word.AudioPath
    };
}
