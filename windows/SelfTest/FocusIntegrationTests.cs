using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;

namespace Lexi;

public static class FocusIntegrationTests
{
    public static Task RunAsync(MainWindow window)
    {
        var lines = new List<string>(); var exit = 1;
        object? Call(string name, params object[] args) => Invoke(window, name, args);
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        void Check(bool value, string label) { if (!value) throw new Exception(label); lines.Add("PASS " + label); }
        try
        {
            var store = Field<IVocabularyArchive>("_vocabService"); store.AddWord("focusfixture", "", "专注测试", "test"); Call("RefreshWords");
            window.FindControl<TextBlock>("ResultWordText")!.Text = "focusfixture";
            window.FindControl<TextBlock>("ResultTranslationText")!.Text = "专注测试";
            window.FindControl<Border>("LookupResultCard")!.IsVisible = true;
            Call("EnterFocusFromLookup"); Check(Field<bool>("_focusActive"), "lookup focus entry opens card");
            window.UpdateLayout();
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96)))
            {
                bitmap.Render(window);
                bitmap.Save(Path.Combine(Environment.GetEnvironmentVariable("LEXI_DATA_DIR")!, "focus-first-card.png"));
            }
            Call("HandleFocusSpace"); Call("RateFocus", StudyRating.Unsure);
            var round = Field<StudyRound<string>>("_focusRound"); Check(round.CurrentTarget == 3 && !round.IsFinished, "unsure enables three recognitions");
            Call("UndoFocus"); Check(round.CurrentTarget == 1, "focus undo restores queue and archive");
            Call("RateFocus", StudyRating.Forgot); Check(store.GetAllWords().Single(w => w.Word == "focusfixture").Stage == 0, "forgot resets archived stage");
            Call("UndoFocus"); Check(round.CurrentTarget == 1, "forgot undo restores recall state");
            Call("RateFocus", StudyRating.Known); Check(round.IsFinished, "first known completes review focus");
            var memoryStore = ServiceFactory.OpenMemory(store);
            var memoryKey = WordKeyResolver.FromArchive(store.GetAllWords().Single(w => w.Word == "focusfixture")).Key;
            Check(memoryStore.GetCard(memoryKey)?.NextReviewAtUtc is not null,
                "completed focus rating writes FSRS due date");
            Call("UndoFocus"); Check(!round.IsFinished, "last-card undo restores finished round");
            Check(memoryStore.GetCard(memoryKey) == null, "focus undo invalidates canonical and restores pre-rating card");
            Call("ExitFocus"); Check(!Field<bool>("_focusActive") && window.FindControl<ScrollViewer>("PageLookup")!.IsVisible, "exit restores lookup visibility");

            var examCatalog = ExamCatalog.Load();
            var examWord = examCatalog.BooksForTopic("cet4").First().Sections.First().Entries.First();
            Call("OpenExamTopic", "cet4");
            Field<HashSet<string>>("_examSelectedWordIds").Clear();
            Field<HashSet<string>>("_examSelectedWordIds").Add(examWord.Id);
            Call("RenderExamWords");
            Call("UpdateExamActions");
            Button FindExamAction(string name) =>
                Field<WrapPanel>("_examFooterActionHost").GetLogicalDescendants().OfType<Button>()
                    .SingleOrDefault(button => button.Name == name)
                ?? throw new InvalidOperationException("Missing exam action " + name);
            Check(new[] { "ExamStartCardsBtn", "ExamStartHintsBtn", "ExamStartDictationBtn" }
                    .All(name => Field<WrapPanel>("_examFooterActionHost").GetLogicalDescendants()
                        .OfType<Button>().Any(button => button.Name == name)),
                "exam topic exposes card, hints and dictation actions");

            FindExamAction("ExamStartCardsBtn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var examPresentation = Field<object?>("_focusPresentation");
            var presentationKey = examPresentation?.GetType().GetProperty("Key")?.GetValue(examPresentation);
            var actualMemoryKey = presentationKey?.GetType().GetProperty("Key")?.GetValue(presentationKey)?.ToString();
            Check(Field<bool>("_focusActive") && Field<TypingSession?>("_learningTypingSession") == null,
                "exam card mode opens focus without a typing session");
            Check(Field<StudyRound<string>>("_focusRound").Current.StartsWith("exam:", StringComparison.Ordinal),
                "exam card mode keeps exam-scoped word identity");
            Check(actualMemoryKey == WordKeyResolver.FromIelts(examWord).Key,
                "exam card mode presents the exam word in FSRS");
            Check(!Field<Border>("_examTopicsPage").IsVisible, "exam card mode hides the topic page");
            Call("ExitFocus");
            Check(Field<Border>("_examTopicsPage").IsVisible && Field<Button>("_examTopicsNav").Classes.Contains("active"),
                "exam focus exit restores topic page and navigation");

            FindExamAction("ExamStartHintsBtn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Field<TypingSession?>("_learningTypingSession")?.Hints == true,
                "exam hints mode starts a hinted typing session");
            Call("ExitLearningTyping");
            FindExamAction("ExamStartDictationBtn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Field<TypingSession?>("_learningTypingSession")?.Hints == false,
                "exam dictation mode starts an unhinted typing session");
            Call("ExitLearningTyping");

            var catalog = IeltsCatalog.Load(); Check(catalog.Sections.Count > 0 && catalog.AllWords.Any(), "IELTS catalog deployed with entries");
            Call("ShowIeltsCatalog"); Call("ShowIeltsResources");
            Check(Field<Grid>("_ieltsSubContent").Children.Count > 0 && Field<Grid>("_ieltsPage").IsVisible,
                "IELTS resources stay on the independent IELTS page");
            Call("ShowIeltsWriting"); Check(catalog.Sentences.Count == 100, "writing resource exercises available");
            Call("ShowIeltsCatalog");
            var workspace = Field<Lexi.Features.Ielts.IeltsWorkspaceControl>("_ieltsWorkspace");
            Check(workspace.GetType().GetField("_selectedSection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace) is LearningSection,
                "IELTS actual chapter selector selects catalog section");
            var entry = catalog.AllWords.First(w => !string.IsNullOrWhiteSpace(w.AudioPath));
            Call("StartIeltsTyping", new List<LearningWord> { entry }, false);
            Field<TextBox>("_learningTypingInput").Text = entry.Word; Call("SubmitLearningTyping");
            Check(Field<LearningProgress>("_ieltsProgress").Typed.Contains(entry.Id), "IELTS typing persists typed word progress");
            Call("ExitLearningTyping");
            var neutral = new LearningWord { Id = "focus-typing-neutral", Words = ["neutral"], Meaning = "中性测试", Phonetic = "/ˈnjuːtrəl/" };
            Call("StartLearningTyping", new List<LearningWord> { neutral }, false, "learning");
            Field<TextBox>("_learningTypingInput").Text = neutral.Word; Call("SubmitLearningTyping");
            Check(!Field<LearningProgress>("_ieltsProgress").Typed.Contains(neutral.Id), "archive typing does not pollute IELTS progress");
            Call("ExitLearningTyping");
            var synonym = catalog.AllWords.First(w => w.Synonyms.Count > 0);
            Call("StartIeltsSynonyms", new List<LearningWord> { synonym });
            Field<TextBox>("_synonymWordInput").Text = synonym.Word;
            Field<TextBox>("_synonymAnswersInput").Text = string.Join(",", synonym.Synonyms); Call("CheckIeltsSynonyms");
            Check(Field<bool>("_synonymCorrect"), "synonym actual UI validates complete synonym answer set");
            Field<TextBox>("_synonymAnswersInput").Text = "wrong"; Call("CheckIeltsSynonyms");
            Check(!Field<bool>("_synonymCorrect") && Field<LearningProgress>("_ieltsProgress").Errors.Contains(synonym.Id), "synonym wrong answer records error progress");
            store.AddWord("focusfixturetwo", "", "第二个词", "test"); Call("RefreshWords");
            Call("EnterArchiveFocus");
            Check(Field<StudyRound<string>>("_focusRound").Total >= 2, "archive focus opens multiword deck");
            Call("HandleFocusSpace"); Call("RateFocus", StudyRating.Known); Call("ReclassifyFocus");
            Check(Field<StudyRound<string>>("_focusRound").Forgot == 1, "focus reclassification changes completed known into forgot");
            Call("ExitFocus");
            Call("ShowPage", "quotes"); Call("EnterArchiveFocus");
            Check(!Field<ScrollViewer>("_quotesPage").IsVisible, "archive focus hides quotes page");
            Call("ExitFocus");
            Check(Field<ScrollViewer>("_quotesPage").IsVisible, "focus exit restores quotes page");
            exit = 0;
        }
        catch (Exception ex) { lines.Add("FAIL " + ex); }
        finally { File.WriteAllLines(Path.Combine(Environment.GetEnvironmentVariable("LEXI_DATA_DIR")!, "focus-integration.txt"), lines); (Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!.Shutdown(exit); }
        return Task.CompletedTask;
    }

    private static object? Invoke(MainWindow window, string name, params object?[] args)
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var methods = typeof(MainWindow).GetMethods(flags).Where(method =>
        {
            if (method.Name != name) return false;
            var parameters = method.GetParameters();
            if (args.Length < parameters.Count(p => !p.IsOptional) || args.Length > parameters.Length) return false;
            return args.Select((arg, index) => arg == null
                ? !parameters[index].ParameterType.IsValueType
                : parameters[index].ParameterType.IsInstanceOfType(arg)).All(valid => valid);
        }).OrderBy(method => method.GetParameters().Length).ToArray();
        if (methods.Length == 0)
            throw new MissingMethodException($"{name} has no overload for {args.Length} arguments");
        var target = methods[0];
        var targetParameters = target.GetParameters();
        var invocation = new object?[targetParameters.Length];
        Array.Copy(args, invocation, args.Length);
        for (var index = args.Length; index < invocation.Length; index++)
            invocation[index] = targetParameters[index].DefaultValue;
        return target.Invoke(window, invocation);
    }
}
