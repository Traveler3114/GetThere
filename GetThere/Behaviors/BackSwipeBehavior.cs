using Microsoft.Maui.Controls;

namespace GetThere.Behaviors;

/// <summary>
/// A behavior that prevents the default back swipe gesture on certain pages.
/// </summary>
public class BackSwipeBehavior : Behavior<VisualElement>
{
    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);
        // Implementation for disabling back swipe could go here if needed per platform
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
        base.OnDetachingFrom(bindable);
    }
}
