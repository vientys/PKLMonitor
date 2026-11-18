using Microsoft.Maui.Controls;

namespace Richikiwir.Views;

public sealed class MaterialIcon : ContentView
{
    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon), typeof(string), typeof(MaterialIcon), "help", propertyChanged: OnIconChanged);
    public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(
        nameof(IconSize), typeof(double), typeof(MaterialIcon), 20d, propertyChanged: OnIconChanged);
    public static readonly BindableProperty IconColorProperty = BindableProperty.Create(
        nameof(IconColor), typeof(Color), typeof(MaterialIcon), Colors.Black, propertyChanged: OnIconChanged);

    private readonly Label _label;

    public MaterialIcon()
    {
        _label = new Label
        {
            FontFamily = "MaterialSymbols",
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap
        };
        Content = _label;
        UpdateIcon();
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public Color IconColor
    {
        get => (Color)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    private static void OnIconChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((MaterialIcon)bindable).UpdateIcon();
    }

    private void UpdateIcon()
    {
        _label.Text = Icon;
        _label.FontSize = IconSize;
        _label.TextColor = IconColor;
        WidthRequest = IconSize * 1.4;
        HeightRequest = IconSize * 1.4;
    }
}
