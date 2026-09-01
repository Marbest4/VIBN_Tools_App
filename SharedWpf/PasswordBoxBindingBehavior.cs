using System.Windows;
using System.Windows.Controls;

namespace VIBN_Tools.SharedWpf;

/// <summary>
/// Provides a two-way MVVM binding for PasswordBox. The value only remains in
/// the view model until the save command has completed and clears its input.
/// </summary>
public static class PasswordBoxBindingBehavior
{
    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating",
        typeof(bool),
        typeof(PasswordBoxBindingBehavior));

    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password",
        typeof(string),
        typeof(PasswordBoxBindingBehavior),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnPasswordChanged));

    public static string GetPassword(DependencyObject element) =>
        (string)element.GetValue(PasswordProperty);

    public static void SetPassword(DependencyObject element, string value) =>
        element.SetValue(PasswordProperty, value);

    private static void OnPasswordChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not PasswordBox passwordBox)
            return;

        passwordBox.PasswordChanged -= OnPasswordBoxPasswordChanged;
        if (!(bool)passwordBox.GetValue(IsUpdatingProperty))
            passwordBox.Password = args.NewValue as string ?? string.Empty;
        passwordBox.PasswordChanged += OnPasswordBoxPasswordChanged;
    }

    private static void OnPasswordBoxPasswordChanged(object sender, RoutedEventArgs args)
    {
        var passwordBox = (PasswordBox)sender;
        passwordBox.SetValue(IsUpdatingProperty, true);
        SetPassword(passwordBox, passwordBox.Password);
        passwordBox.SetValue(IsUpdatingProperty, false);
    }
}
