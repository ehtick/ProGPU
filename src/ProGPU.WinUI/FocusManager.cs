using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Scene;

namespace ProGPU.WinUI;

public enum FocusNavigationDirection
{
    Next,
    Previous,
    Up,
    Down,
    Left,
    Right
}

public static class FocusManager
{
    public static FrameworkElement? GetFocusedElement()
    {
        return InputSystem.FocusedElement;
    }

    private static Vector2 GetGlobalPosition(Visual? visual)
    {
        if (visual == null) return Vector2.Zero;
        Vector2 globalOffset = Vector2.Zero;
        Visual? current = visual;
        while (current != null)
        {
            globalOffset += current.Offset;
            current = current.Parent;
        }
        return globalOffset;
    }

    private static Vector2 GetGlobalCenter(FrameworkElement element)
    {
        return GetGlobalPosition(element) + element.Size / 2f;
    }

    private static Rect GetGlobalBounds(FrameworkElement element)
    {
        return new Rect(GetGlobalPosition(element), element.Size);
    }

    private static void GatherFocusableElements(Visual visual, List<FrameworkElement> list)
    {
        if (visual is not FrameworkElement fe || !fe.IsEnabled)
            return;

        if (fe is Control ctrl && ctrl.IsTabStop)
        {
            list.Add(fe);
        }

        if (visual is ContainerVisual container)
        {
            foreach (var child in container.Children)
            {
                GatherFocusableElements(child, list);
            }
        }
    }

    public static bool TryMoveFocus(FocusNavigationDirection direction)
    {
        if (InputSystem.Root == null) return false;

        var focusableElements = new List<FrameworkElement>();
        GatherFocusableElements(InputSystem.Root, focusableElements);
        if (focusableElements.Count == 0) return false;

        if (direction == FocusNavigationDirection.Next)
        {
            InputSystem.CycleFocus(false);
            return true;
        }
        if (direction == FocusNavigationDirection.Previous)
        {
            InputSystem.CycleFocus(true);
            return true;
        }

        var current = InputSystem.FocusedElement;
        if (current == null)
        {
            // If no element is focused, focus the first available focusable element
            InputSystem.SetFocus(focusableElements[0]);
            return true;
        }

        Vector2 currentCenter = GetGlobalCenter(current);
        Rect currentBounds = GetGlobalBounds(current);

        FrameworkElement? bestCandidate = null;
        float bestScore = float.MaxValue;

        foreach (var cand in focusableElements)
        {
            if (cand == current) continue;

            Vector2 candCenter = GetGlobalCenter(cand);
            Rect candBounds = GetGlobalBounds(cand);

            bool inDirection = false;
            bool withinTolerance = false;

            switch (direction)
            {
                case FocusNavigationDirection.Up:
                    inDirection = candCenter.Y < currentCenter.Y;
                    if (inDirection)
                    {
                        float maxTolerance = Math.Max(currentBounds.Width, candBounds.Width) * 1.5f + 100f;
                        withinTolerance = Math.Abs(candCenter.X - currentCenter.X) <= maxTolerance;
                    }
                    break;

                case FocusNavigationDirection.Down:
                    inDirection = candCenter.Y > currentCenter.Y;
                    if (inDirection)
                    {
                        float maxTolerance = Math.Max(currentBounds.Width, candBounds.Width) * 1.5f + 100f;
                        withinTolerance = Math.Abs(candCenter.X - currentCenter.X) <= maxTolerance;
                    }
                    break;

                case FocusNavigationDirection.Left:
                    inDirection = candCenter.X < currentCenter.X;
                    if (inDirection)
                    {
                        float maxTolerance = Math.Max(currentBounds.Height, candBounds.Height) * 1.5f + 100f;
                        withinTolerance = Math.Abs(candCenter.Y - currentCenter.Y) <= maxTolerance;
                    }
                    break;

                case FocusNavigationDirection.Right:
                    inDirection = candCenter.X > currentCenter.X;
                    if (inDirection)
                    {
                        float maxTolerance = Math.Max(currentBounds.Height, candBounds.Height) * 1.5f + 100f;
                        withinTolerance = Math.Abs(candCenter.Y - currentCenter.Y) <= maxTolerance;
                    }
                    break;
            }

            if (inDirection && withinTolerance)
            {
                float score = Vector2.Distance(currentCenter, candCenter);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestCandidate = cand;
                }
            }
        }

        if (bestCandidate != null)
        {
            InputSystem.SetFocus(bestCandidate);
            return true;
        }

        return false;
    }
}
