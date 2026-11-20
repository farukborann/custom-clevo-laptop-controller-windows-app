using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BSCustomClevoController.Views
{
    internal static class UiFocusHelper
    {
        public static void ClearTextBoxFocusOnClick(Window window, object originalSource)
        {
            if (window == null || !(Keyboard.FocusedElement is DependencyObject focused))
            {
                return;
            }

            TextBox textBox = FindAncestor<TextBox>(focused);
            if (textBox == null)
            {
                return;
            }

            DependencyObject clicked = originalSource as DependencyObject;
            if (!IsDescendantOrSelf(clicked, textBox))
            {
                window.Focus();
            }
        }

        private static T FindAncestor<T>(DependencyObject node) where T : DependencyObject
        {
            DependencyObject current = node;
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static bool IsDescendantOrSelf(DependencyObject node, DependencyObject target)
        {
            DependencyObject current = node;
            while (current != null)
            {
                if (current == target)
                {
                    return true;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }
    }
}

