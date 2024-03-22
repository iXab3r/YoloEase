namespace YoloEase.UI.Dto;

public sealed record RectangleD
{
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Width { get; private set; }
    public double Height { get; private set; }

    public RectangleD(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double CenterX
    {
        get { return X + Width / 2.0; }
    }

    public double CenterY
    {
        get { return Y + Height / 2.0; }
    }

    public RectangleD Scale(int imageWidth, int imageHeight)
    {
        return new RectangleD(X * imageWidth, Y * imageHeight, Width * imageWidth, Height * imageHeight);
    }

    public static RectangleD FromYolo(double centerX, double centerY, double width, double height)
    {
        double x = centerX - width / 2.0;
        double y = centerY - height / 2.0;
        return new RectangleD(x, y, width, height);
    }

    public static RectangleD FromLTRB(double left, double top, double right, double bottom)
    {
        double x = left;
        double y = top;
        double width = right - left;
        double height = bottom - top;
        return new RectangleD(x, y, width, height);
    }
}