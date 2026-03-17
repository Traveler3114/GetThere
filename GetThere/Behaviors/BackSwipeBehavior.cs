using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace GetThere.Behaviors;

/// <summary>
/// A reusable behavior that adds a right-swipe gesture to a page or layout 
/// to perform back navigation (Shell.Current.GoToAsync("..")).
/// </summary>
public class BackSwipeBehavior : Behavior<View>
{
    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);

        var swipeGesture = new SwipeGestureRecognizer
        {
            Direction = SwipeDirection.Right
        };
        swipeGesture.Swiped += OnSwiped;

        bindable.GestureRecognizers.Add(swipeGesture);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        base.OnDetachingFrom(bindable);

        for (int i = bindable.GestureRecognizers.Count - 1; i >= 0; i--)
        {
            if (bindable.GestureRecognizers[i] is SwipeGestureRecognizer swipe && 
                swipe.Direction == SwipeDirection.Right)
            {
                swipe.Swiped -= OnSwiped;
                bindable.GestureRecognizers.RemoveAt(i);
            }
        }
    }

    private async void OnSwiped(object? sender, SwipedEventArgs e)
    {
        if (Shell.Current == null) return;

        try
        {
            // 1. Check Modal stack (if a page is opened as a Pop-up)
            if (Shell.Current.Navigation.ModalStack.Count > 0)
            {
                await Shell.Current.Navigation.PopModalAsync();
                return;
            }

            // 2. Check regular Navigation stack
            // NavigationStack[0] is the root. If Count > 1, we can go back.
            if (Shell.Current.Navigation.NavigationStack.Count > 1)
            {
                await Shell.Current.GoToAsync("..");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Korisnik je na početnoj stranici, nema povratka.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Greška u univerzalnoj navigaciji: {ex.Message}");
        }
    }
}
