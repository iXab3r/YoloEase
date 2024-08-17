using YoloEase.Cvat.Shared;

namespace YoloEase.UI.Scaffolding;

public static class CvatRectangleDExtensions
{
    public static SharpRectangleF ToSharpRectangleF(this CvatRectangleD rectangle)
    {
        return new SharpRectangleF((float) rectangle.X, (float) rectangle.Y, (float) rectangle.Width, (float) rectangle.Height);
    }
    
    public static SharpSize ToSharpSize(this CvatRectangleD rectangle)
    {
        return new SharpSize((int) rectangle.Width, (int) rectangle.Height);
    }
}