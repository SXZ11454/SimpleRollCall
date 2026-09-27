using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SimpleRollCall;

/// <summary>
/// UWP style press feedback: the control under the cursor shrinks to 0.95x while it
/// is held down (0.1s) and springs back to 1.0 when it is released.
///
/// Attach <c>local:PressScale.Enabled="True"</c> to a container (the Window is enough).
/// The press is picked up through the tunneling preview events of that container and,
/// additionally, through the global input pipeline - the latter reaches controls that
/// live in a popup (flyout, menu, dialog), which never routes through the window.
///
/// Covered are all pressable controls: every <see cref="ButtonBase"/> (Button,
/// ToggleButton, RepeatButton - which includes check boxes, radio buttons, the combo
/// box drop-down and the number box spin buttons) and menu items. Not covered on
/// purpose: the window caption buttons (ModernWpf's TitleBarButton), the toggle
/// switches (they slide instead of pressing down), and clicks that only place the
/// caret in a text box or editable combo box text.
/// </summary>
public static class PressScale
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(PressScale),
        new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

    private const double PressedScale = 0.95;

    private static readonly Duration PressDuration = new(TimeSpan.FromMilliseconds(100));
    private static readonly Duration ReleaseDuration = new(TimeSpan.FromMilliseconds(150));

    /// <summary>Controls that are currently scaled down (UI thread only).</summary>
    private static readonly HashSet<UIElement> Pressed = new();

    /// <summary>Number of containers the property is enabled on (the global input hook is shared).</summary>
    private static int _attachedCount;

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.PreviewMouseLeftButtonDown += OnContainerMouseDown;
            element.PreviewMouseLeftButtonUp += OnContainerMouseUp;
            if (_attachedCount++ == 0)
            {
                InputManager.Current.PostProcessInput += OnPostProcessInput;
            }
        }
        else
        {
            element.PreviewMouseLeftButtonDown -= OnContainerMouseDown;
            element.PreviewMouseLeftButtonUp -= OnContainerMouseUp;
            if (_attachedCount > 0 && --_attachedCount == 0)
            {
                InputManager.Current.PostProcessInput -= OnPostProcessInput;
            }
        }
    }

    // ---------- press detection ----------

    private static void OnContainerMouseDown(object sender, MouseButtonEventArgs e) => HandlePress(e);

    private static void OnContainerMouseUp(object sender, MouseButtonEventArgs e) => ReleaseAll();

    /// <summary>
    /// Fires for every input of the UI thread, including events inside popups, so
    /// buttons in flyouts, menus and dialogs animate too. The container handlers
    /// above may already have run - <see cref="Press"/> and <see cref="Release"/>
    /// are idempotent, so the second call is harmless.
    /// </summary>
    private static void OnPostProcessInput(object sender, ProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is not MouseButtonEventArgs mouse || mouse.ChangedButton != MouseButton.Left)
        {
            return;
        }

        // Both the preview and the bubbling stage of the event reach this hook; either
        // one is enough, and handling both is harmless because the work is idempotent.
        if (ReferenceEquals(mouse.RoutedEvent, UIElement.PreviewMouseLeftButtonDownEvent) ||
            ReferenceEquals(mouse.RoutedEvent, UIElement.MouseLeftButtonDownEvent) ||
            ReferenceEquals(mouse.RoutedEvent, Mouse.PreviewMouseDownEvent) ||
            ReferenceEquals(mouse.RoutedEvent, Mouse.MouseDownEvent))
        {
            HandlePress(mouse);
        }
        else if (ReferenceEquals(mouse.RoutedEvent, UIElement.PreviewMouseLeftButtonUpEvent) ||
                 ReferenceEquals(mouse.RoutedEvent, UIElement.MouseLeftButtonUpEvent) ||
                 ReferenceEquals(mouse.RoutedEvent, Mouse.PreviewMouseUpEvent) ||
                 ReferenceEquals(mouse.RoutedEvent, Mouse.MouseUpEvent))
        {
            ReleaseAll();
        }
    }

    private static void HandlePress(MouseButtonEventArgs e)
    {
        if (FindPressable(e.OriginalSource as DependencyObject) is { } target && target.IsEnabled)
        {
            Press(target);
        }
    }

    private static void ReleaseAll()
    {
        foreach (UIElement element in Pressed.ToArray())
        {
            Release(element);
        }
    }

    /// <summary>
    /// Walks up from the hit element to the nearest pressable control.
    /// </summary>
    private static UIElement? FindPressable(DependencyObject? source)
    {
        while (source is not null)
        {
            // The caption buttons (minimise / maximise / close) keep their own behaviour
            if (source.GetType().Name.Contains("TitleBar", StringComparison.Ordinal))
            {
                return null;
            }

            switch (source)
            {
                case ButtonBase button:
                    // The drop-down toggle of a combo box spans the whole control, so
                    // animate the combo box itself - border and text shrink together.
                    if (button is ToggleButton && FindAncestor<ComboBox>(button) is { } combo)
                    {
                        return combo;
                    }

                    return button;

                // Pressables that do not derive from ButtonBase. ToggleSwitch is left
                // out on purpose: it has its own slide animation and no press feedback.
                case MenuItem:
                    return (UIElement)source;
            }

            source = GetParent(source);
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        for (var current = element; current is not null; current = GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject obj)
    {
        if (obj is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(obj);
        }

        return obj is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(obj);
    }

    // ---------- animation ----------

    private static void Press(UIElement button)
    {
        ScaleTransform scale = EnsureScale(button);
        Animate(scale, PressedScale, PressDuration, new QuadraticEase { EasingMode = EasingMode.EaseOut });

        if (Pressed.Add(button))
        {
            // Safety net: the button releases mouse capture when the click finishes,
            // which also covers releases that never reach any of the other handlers.
            button.PreviewMouseLeftButtonUp += OnButtonMouseUp;
            button.LostMouseCapture += OnButtonLostCapture;
        }
    }

    private static void Release(UIElement button)
    {
        if (!Pressed.Remove(button))
        {
            return;
        }

        button.PreviewMouseLeftButtonUp -= OnButtonMouseUp;
        button.LostMouseCapture -= OnButtonLostCapture;

        ScaleTransform scale = EnsureScale(button);
        Animate(scale, 1.0, ReleaseDuration, new BackEase { Amplitude = 1.5, EasingMode = EasingMode.EaseOut });
    }

    private static void OnButtonMouseUp(object sender, MouseButtonEventArgs e) => Release((UIElement)sender);

    private static void OnButtonLostCapture(object sender, MouseEventArgs e) => Release((UIElement)sender);

    /// <summary>
    /// Returns a scale transform the animation may write to. Styles and templates
    /// share (and freeze) their values, so a frozen transform is never animated -
    /// a fresh one is placed alongside it instead.
    /// </summary>
    private static ScaleTransform EnsureScale(UIElement element)
    {
        Transform current = element.RenderTransform;

        ScaleTransform? scale = current switch
        {
            ScaleTransform existing => existing,
            TransformGroup group => group.Children.OfType<ScaleTransform>().FirstOrDefault(),
            _ => null
        };

        if (scale is not null && !scale.IsFrozen)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            return scale;
        }

        scale = new ScaleTransform(1, 1);
        if (current is null)
        {
            element.RenderTransform = scale;
        }
        else
        {
            TransformGroup group = current is TransformGroup existingGroup && !existingGroup.IsFrozen
                ? existingGroup
                : new TransformGroup { Children = { current } };
            if (!group.Children.Contains(scale))
            {
                group.Children.Add(scale);
            }

            element.RenderTransform = group;
        }

        element.RenderTransformOrigin = new Point(0.5, 0.5);
        return scale;
    }

    private static void Animate(ScaleTransform scale, double to, Duration duration, IEasingFunction easing)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateAnimation(to, duration, easing));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateAnimation(to, duration, easing));
    }

    private static DoubleAnimation CreateAnimation(double to, Duration duration, IEasingFunction easing) =>
        new(to, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };
}
