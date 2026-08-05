using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RouterPilot.Controls;

public partial class LeaderboardControl : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(LeaderboardControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(
            nameof(Subtitle),
            typeof(string),
            typeof(LeaderboardControl),
            new PropertyMetadata(string.Empty, OnOptionalTextChanged));

    public static readonly DependencyProperty HeaderTextProperty =
        DependencyProperty.Register(
            nameof(HeaderText),
            typeof(string),
            typeof(LeaderboardControl),
            new PropertyMetadata(string.Empty, OnHeaderTextChanged));

    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(
            nameof(EmptyText),
            typeof(string),
            typeof(LeaderboardControl),
            new PropertyMetadata("No data is available yet."));

    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(
            nameof(Items),
            typeof(IEnumerable),
            typeof(LeaderboardControl),
            new PropertyMetadata(null, OnItemsChanged));

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(
            nameof(AccentBrush),
            typeof(Brush),
            typeof(LeaderboardControl),
            new PropertyMetadata(Brushes.RoyalBlue));

    private static readonly DependencyPropertyKey SubtitleVisibilityPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(SubtitleVisibility),
            typeof(Visibility),
            typeof(LeaderboardControl),
            new PropertyMetadata(Visibility.Collapsed));

    public static readonly DependencyProperty SubtitleVisibilityProperty =
        SubtitleVisibilityPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey HeaderVisibilityPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HeaderVisibility),
            typeof(Visibility),
            typeof(LeaderboardControl),
            new PropertyMetadata(Visibility.Collapsed));

    public static readonly DependencyProperty HeaderVisibilityProperty =
        HeaderVisibilityPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey EmptyVisibilityPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(EmptyVisibility),
            typeof(Visibility),
            typeof(LeaderboardControl),
            new PropertyMetadata(Visibility.Visible));

    public static readonly DependencyProperty EmptyVisibilityProperty =
        EmptyVisibilityPropertyKey.DependencyProperty;

    private INotifyCollectionChanged? _observableItems;

    public LeaderboardControl()
    {
        InitializeComponent();
        UpdateVisibility();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string HeaderText
    {
        get => (string)GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Visibility SubtitleVisibility =>
        (Visibility)GetValue(SubtitleVisibilityProperty);

    public Visibility HeaderVisibility =>
        (Visibility)GetValue(HeaderVisibilityProperty);

    public Visibility EmptyVisibility =>
        (Visibility)GetValue(EmptyVisibilityProperty);

    private static void OnOptionalTextChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        ((LeaderboardControl)dependencyObject).SetValue(
            SubtitleVisibilityPropertyKey,
            HasText(args.NewValue) ? Visibility.Visible : Visibility.Collapsed);
    }

    private static void OnHeaderTextChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        ((LeaderboardControl)dependencyObject).SetValue(
            HeaderVisibilityPropertyKey,
            HasText(args.NewValue) ? Visibility.Visible : Visibility.Collapsed);
    }

    private static void OnItemsChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var control = (LeaderboardControl)dependencyObject;
        control.DetachCollectionChanged(args.OldValue);
        control.AttachCollectionChanged(args.NewValue);
        control.UpdateVisibility();
    }

    private void AttachCollectionChanged(object? items)
    {
        _observableItems = items as INotifyCollectionChanged;

        if (_observableItems is not null)
        {
            _observableItems.CollectionChanged += OnCollectionChanged;
        }
    }

    private void DetachCollectionChanged(object? items)
    {
        if (_observableItems is not null)
        {
            _observableItems.CollectionChanged -= OnCollectionChanged;
            _observableItems = null;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        SetValue(
            SubtitleVisibilityPropertyKey,
            HasText(Subtitle) ? Visibility.Visible : Visibility.Collapsed);
        SetValue(
            HeaderVisibilityPropertyKey,
            HasText(HeaderText) ? Visibility.Visible : Visibility.Collapsed);
        SetValue(
            EmptyVisibilityPropertyKey,
            HasAnyItems(Items) ? Visibility.Collapsed : Visibility.Visible);
    }

    private static bool HasAnyItems(IEnumerable? items)
    {
        if (items is null)
        {
            return false;
        }

        var enumerator = items.GetEnumerator();

        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    private static bool HasText(object? value) =>
        value is string text && !string.IsNullOrWhiteSpace(text);
}
