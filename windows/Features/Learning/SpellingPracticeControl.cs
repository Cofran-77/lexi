using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lexi.Controls;

namespace Lexi.Features.Learning;

/// <summary>Display state for the full-screen typing surface.</summary>
public sealed class SpellingPracticeModel
{
    public string Word { get; set; } = "";
    public string Input { get; set; } = "";
    public string FailedInput { get; set; } = "";
    public bool Hints { get; set; } = true;
    public TypingOutcome Outcome { get; set; } = TypingOutcome.Pending;
    public string Phonetic { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Hint { get; set; } = "";
    public string Feedback { get; set; } = "";
    public bool? IsCorrect { get; set; }
    public bool AnswerRevealed { get; set; }
    public string ProgressText { get; set; } = "";
    public Action? Speak { get; set; }
}

/// <summary>
/// Windows spelling surface aligned with the macOS word-card interaction:
/// full-screen host gradient, centered colored letters, and compact bottom actions.
/// </summary>
public class SpellingPracticeControl : UserControl
{
    public event Action<string>? InputChanged;
    public event Action<string>? Submitted;
    public event Action? HintRequested;
    public event Action? AnswerRequested;
    public event Action? SpeakRequested;
    public event Action? NextRequested;
    public event Action? CancelRequested;

    public Action<string>? OnSubmit { get; set; }
    public Action? OnHint { get; set; }
    public Action? OnRevealAnswer { get; set; }
    public Action? OnSpeak { get; set; }
    public Action? OnNext { get; set; }
    public Action? OnCancel { get; set; }

    private static readonly FontFamily SpellingFont =
        new("Cascadia Mono, Consolas, Menlo, Microsoft YaHei UI, Segoe UI");

    private readonly TextBlock _progressBlock;
    private readonly SelectableTextBlock _meaningBlock;
    private readonly SelectableTextBlock _phoneticBlock;
    private readonly StackPanel _phoneticRow;
    private readonly SelectableTextBlock _hintBlock;
    private readonly TextBox _inputBox;
    private readonly StackPanel _lettersPanel;
    private readonly SelectableTextBlock _answerBlock;
    private readonly SelectableTextBlock _feedbackBlock;
    private readonly Button _hintButton;
    private readonly Button _answerButton;
    private readonly Button _submitButton;
    private readonly Button _nextButton;
    private readonly Button _speakButton;
    private readonly Button _cancelButton;
    private readonly InlineAudioButton _speakerButton;
    private readonly Grid _wordGrid;

    private SpellingPracticeModel _currentModel = new();
    private bool _updatingInput;
    private bool _isSubmitting;

    public TextBox InputBox => _inputBox;
    public StackPanel LettersPanel => _lettersPanel;
    public SelectableTextBlock AnswerBlock => _answerBlock;
    public Button HintButton => _hintButton;
    public Button AnswerButton => _answerButton;
    public Button SubmitButton => _submitButton;
    public Button NextButton => _nextButton;
    public Button SpeakButton => _speakButton;
    public SelectableTextBlock MeaningBlock => _meaningBlock;
    public SelectableTextBlock PhoneticBlock => _phoneticBlock;
    public SelectableTextBlock HintBlock => _hintBlock;
    public SelectableTextBlock FeedbackBlock => _feedbackBlock;
    public SpellingPracticeModel CurrentModel => _currentModel;

    public SpellingPracticeControl()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(32, 22, 32, 22)
        };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _progressBlock = new TextBlock
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        _progressBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        top.Children.Add(_progressBlock);

        var shortcutHint = new TextBlock
        {
            Text = "Esc   Alt+T   Enter",
            FontSize = 12,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center
        };
        shortcutHint.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        Grid.SetColumn(shortcutHint, 1);
        top.Children.Add(shortcutHint);
        root.Children.Add(top);

        var center = new Grid
        {
            MaxWidth = 980,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 18, 12, 18)
        };
        var content = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _meaningBlock = new SelectableTextBlock
        {
            FontSize = 19,
            FontWeight = FontWeight.Medium,
            LineHeight = 27,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 860
        };
        _meaningBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InkBrush"));
        content.Children.Add(_meaningBlock);

        _wordGrid = new Grid
        {
            MinHeight = 96,
            Margin = new Thickness(0, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _lettersPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        _wordGrid.Children.Add(_lettersPanel);

        _inputBox = new TextBox
        {
            Name = "SpellingInput",
            Classes = { "spelling-input" },
            FontFamily = SpellingFont,
            FontSize = 46,
            FontWeight = FontWeight.Medium,
            MinWidth = 0,
            MinHeight = 0,
            Padding = default,
            BorderThickness = default,
            Background = Brushes.Transparent,
            Foreground = Brushes.Transparent,
            SelectionBrush = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            CaretBrush = new SolidColorBrush(Color.Parse("#268D98")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsUndoEnabled = false
        };
        _inputBox.TextChanged += (_, _) =>
        {
            if (_updatingInput) return;
            InputChanged?.Invoke(_inputBox.Text ?? "");
        };
        _inputBox.PastingFromClipboard += (_, e) => e.Handled = true;
        _wordGrid.Children.Add(_inputBox);
        content.Children.Add(_wordGrid);

        _answerBlock = new SelectableTextBlock
        {
            Name = "SpellingAnswer",
            FontFamily = SpellingFont,
            FontSize = 34,
            FontWeight = FontWeight.Medium,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = false
        };
        _answerBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("PrimaryGreen"));
        content.Children.Add(_answerBlock);

        _phoneticRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 7,
            IsVisible = false
        };
        _phoneticBlock = new SelectableTextBlock
        {
            FontSize = 15,
            Opacity = 0.88,
            VerticalAlignment = VerticalAlignment.Center
        };
        _phoneticBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        _phoneticRow.Children.Add(_phoneticBlock);
        _speakerButton = new InlineAudioButton(UiText.Bilingual("朗读单词", "Listen to word"), RequestSpeak);
        _phoneticRow.Children.Add(_speakerButton);
        content.Children.Add(_phoneticRow);

        _hintBlock = new SelectableTextBlock
        {
            Name = "SpellingHint",
            FontFamily = SpellingFont,
            FontSize = 16,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.86,
            IsVisible = false
        };
        _hintBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        content.Children.Add(_hintBlock);

        var assistActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _hintButton = CreatePillButton("提示", "SpellingHintButton");
        _hintButton.Click += (_, _) => RequestHint();
        assistActions.Children.Add(_hintButton);
        _answerButton = CreatePillButton("看答案", "SpellingAnswerButton");
        _answerButton.Click += (_, _) => RequestAnswer();
        assistActions.Children.Add(_answerButton);
        content.Children.Add(assistActions);

        _feedbackBlock = new SelectableTextBlock
        {
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 720,
            IsVisible = false
        };
        content.Children.Add(_feedbackBlock);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = center
        };
        center.Children.Add(content);
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,*"),
            ColumnSpacing = 12
        };
        _cancelButton = CreatePillButton("✕  Esc", "SpellingExitButton");
        _cancelButton.HorizontalAlignment = HorizontalAlignment.Left;
        _cancelButton.Click += (_, _) => RequestCancel();
        footer.Children.Add(_cancelButton);

        _speakButton = CreatePillButton("▷  Alt+T", "SpellingSpeakButton");
        _speakButton.Click += (_, _) => RequestSpeak();
        Grid.SetColumn(_speakButton, 1);
        footer.Children.Add(_speakButton);

        var advanceActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _submitButton = CreatePrimaryPill("✓  ↵", "SpellingSubmitButton");
        _submitButton.Click += (_, _) => SubmitCurrentInput();
        advanceActions.Children.Add(_submitButton);
        _nextButton = CreatePrimaryPill("→  ↵", "SpellingNextButton");
        _nextButton.IsVisible = false;
        _nextButton.Click += (_, _) => RequestNext();
        advanceActions.Children.Add(_nextButton);
        Grid.SetColumn(advanceActions, 2);
        footer.Children.Add(advanceActions);

        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;

        _wordGrid.PointerPressed += (_, _) => FocusInput();
        root.AddHandler(KeyDownEvent, OnRootKeyDown, RoutingStrategies.Tunnel);
        SizeChanged += (_, _) => UpdateWordSizing();
        RefreshLanguage();
    }

    private static Button CreatePillButton(string text, string name) => new()
    {
        Name = name,
        Content = text,
        Classes = { "ghost" },
        CornerRadius = new CornerRadius(22),
        Padding = new Thickness(18, 8),
        FontSize = 14
    };

    private static Button CreatePrimaryPill(string text, string name)
    {
        var button = CreatePillButton(text, name);
        button.Classes.Add("primary");
        return button;
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            RequestCancel();
            return;
        }
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.T or Key.Space)
        {
            e.Handled = true;
            RequestSpeak();
            FocusInput();
            return;
        }
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            SubmitCurrentInput();
        }
    }

    private void RequestHint()
    {
        if (!_updatingInput && !string.IsNullOrEmpty(_inputBox.Text))
            InputChanged?.Invoke(_inputBox.Text);
        HintRequested?.Invoke();
        OnHint?.Invoke();
        FocusInput();
    }

    private void RequestAnswer()
    {
        AnswerRequested?.Invoke();
        OnRevealAnswer?.Invoke();
        FocusInput();
    }

    private void RequestSpeak()
    {
        SpeakRequested?.Invoke();
        OnSpeak?.Invoke();
        _currentModel.Speak?.Invoke();
    }

    private void RequestCancel()
    {
        CancelRequested?.Invoke();
        OnCancel?.Invoke();
    }

    private void RequestNext()
    {
        NextRequested?.Invoke();
        OnNext?.Invoke();
    }

    public void SubmitCurrentInput()
    {
        if (_isSubmitting) return;
        if (_currentModel.Outcome == TypingOutcome.Correct)
        {
            RequestNext();
            return;
        }
        _isSubmitting = true;
        try
        {
            var text = _inputBox.Text ?? "";
            Submitted?.Invoke(text);
            OnSubmit?.Invoke(text);
        }
        finally
        {
            _isSubmitting = false;
            FocusInput();
        }
    }

    public void Update(SpellingPracticeModel model)
    {
        _currentModel = model ?? new SpellingPracticeModel();
        _inputBox.CaretBrush = _currentModel.Hints
            ? Brushes.Transparent
            : new SolidColorBrush(Color.Parse("#268D98"));
        _progressBlock.Text = _currentModel.ProgressText;
        _progressBlock.IsVisible = !string.IsNullOrWhiteSpace(_currentModel.ProgressText);
        _meaningBlock.Text = _currentModel.Meaning;
        _meaningBlock.IsVisible = !string.IsNullOrWhiteSpace(_currentModel.Meaning);

        _phoneticBlock.Text = string.IsNullOrWhiteSpace(_currentModel.Phonetic)
            ? ""
            : $"/{_currentModel.Phonetic.Trim().Trim('/')}/";
        _phoneticRow.IsVisible = !string.IsNullOrWhiteSpace(_phoneticBlock.Text);

        _hintBlock.Text = _currentModel.Hint;
        _hintBlock.IsVisible = !string.IsNullOrWhiteSpace(_currentModel.Hint);

        _feedbackBlock.Text = _currentModel.Feedback;
        _feedbackBlock.IsVisible = !string.IsNullOrWhiteSpace(_currentModel.Feedback);
        _feedbackBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(
            _currentModel.IsCorrect switch
            {
                true => "PrimaryGreen",
                false => "DangerBrush",
                _ => "MutedBrush"
            }));

        var answerVisible = _currentModel.AnswerRevealed ||
            (!_currentModel.Hints && _currentModel.Outcome == TypingOutcome.Retry);
        _answerBlock.Text = answerVisible ? TypingSession.Normalize(_currentModel.Word) : "";
        _answerBlock.IsVisible = answerVisible && !string.IsNullOrWhiteSpace(_answerBlock.Text);

        _updatingInput = true;
        var input = _currentModel.Input;
        if (!string.Equals(_inputBox.Text, input, StringComparison.Ordinal))
            _inputBox.Text = input;
        _updatingInput = false;
        _inputBox.IsReadOnly = _currentModel.Outcome == TypingOutcome.Correct;

        var canAdvance = _currentModel.Outcome == TypingOutcome.Correct;
        _submitButton.IsVisible = !canAdvance;
        _nextButton.IsVisible = canAdvance;
        _hintButton.IsEnabled = _currentModel.Outcome is not (TypingOutcome.Correct or TypingOutcome.Finished);
        _answerButton.IsEnabled = _currentModel.Outcome is not (TypingOutcome.Correct or TypingOutcome.Finished);

        RenderLetters();
        UpdateWordSizing();
        Dispatcher.UIThread.Post(FocusInput);
    }

    private void RenderLetters()
    {
        _lettersPanel.Children.Clear();
        var target = TypingSession.Normalize(_currentModel.Word);
        if (string.IsNullOrEmpty(target)) return;

        var input = _currentModel.Outcome == TypingOutcome.Retry &&
                    string.IsNullOrEmpty(_currentModel.Input)
            ? _currentModel.FailedInput
            : _currentModel.Input;
        var letters = TypingFeedbackModel.Build(
            target,
            input,
            _currentModel.Hints,
            _currentModel.Outcome);
        foreach (var letter in letters)
        {
            var text = new TextBlock
            {
                Text = letter.Character == ' ' ? "·" : letter.Character.ToString(),
                FontFamily = SpellingFont,
                FontWeight = FontWeight.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(
                letter.Tone switch
                {
                    TypingLetterTone.Correct => "PrimaryGreen",
                    TypingLetterTone.Wrong => "DangerBrush",
                    TypingLetterTone.Neutral => "InkBrush",
                    _ => "MutedBrush"
                }));
            if (letter.Tone == TypingLetterTone.Hint) text.Opacity = 0.5;
            _lettersPanel.Children.Add(text);
        }
    }

    private void UpdateWordSizing()
    {
        if (_lettersPanel.Children.Count == 0) return;
        var width = Bounds.Width;
        if (!double.IsFinite(width) || width <= 0) width = Width;
        var available = double.IsFinite(width) && width > 0 ? Math.Max(260, width - 120) : 0;
        var count = Math.Max(1, _lettersPanel.Children.Count);
        var fontSize = available > 0 ? Math.Clamp(available / (count * 0.63 + 1), 22, 58) : 26;
        foreach (var text in _lettersPanel.Children.OfType<TextBlock>()) text.FontSize = fontSize;
        _inputBox.FontSize = fontSize;
        var inputWidth = Math.Max(44, count * fontSize * 0.63 + 10);
        _inputBox.Width = available > 0 ? Math.Min(available, inputWidth) : inputWidth;
        _inputBox.Height = fontSize * 1.55;
        _wordGrid.MinHeight = Math.Max(86, fontSize * 1.7);
        _answerBlock.FontSize = Math.Clamp(fontSize * 0.74, 24, 42);
        _inputBox.CaretIndex = _inputBox.Text?.Length ?? 0;
    }

    public void FocusInput()
    {
        if (!_inputBox.IsEffectivelyEnabled) return;
        _inputBox.Focus();
        _inputBox.CaretIndex = _inputBox.Text?.Length ?? 0;
    }

    public void RefreshLanguage()
    {
        _hintButton.Content = UiText.Bilingual("提示", "Hint");
        _answerButton.Content = UiText.Bilingual("看答案", "Reveal answer");
        _inputBox.Watermark = "";
    }
}
