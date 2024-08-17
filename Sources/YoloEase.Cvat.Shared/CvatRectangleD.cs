namespace YoloEase.Cvat.Shared;

public readonly record struct CvatRectangleD
{
	public CvatRectangleD(double x, double y, double width, double height)
	{
		X = x;
		Y = y;
		Width = width;
		Height = height;
	}

	public double X { get; init; }
	public double Y { get; init; }
	public double Width { get; init; }
	public double Height { get; init; }

	public double CenterX => X + Width / 2;

	public double CenterY => Y + Height / 2;

	/// <summary>
	/// The first number represents the normalized x-coordinate of the center of the bounding box relative to the width of the image. In your example, the x-coordinate is approximately 0.695, indicating that the center of the bounding box is located around 69.5% of the image width.
	/// The second number represents the normalized y-coordinate of the center of the bounding box relative to the height of the image.In your example, the y-coordinate is approximately 0.645, indicating that the center of the bounding box is located around 64.5% of the image height.
	/// The third number represents the normalized width of the bounding box relative to the width of the image.In your example, the width is approximately 0.0325, indicating that the bounding box width is around 3.25% of the image width.
	/// The fourth number represents the normalized height of the bounding box relative to the height of the image. In your example, the height is approximately 0.00664, indicating that the bounding box height is around 0.664% of the image height.
	/// These numbers together specify the position and size of the bounding box that encloses an object within an image. The normalized values allow the annotations to be consistent across different image sizes and resolutions.
	/// e.g. 
	/// 0.6949948072433472 0.6448148488998413 0.03246873617172241 0.006638884544372559
	/// </summary>
	public static CvatRectangleD FromYolo(double centerX, double centerY, double width, double height) =>
			new CvatRectangleD(x: centerX - width / 2, y: centerY - height / 2, width, height);

	public static CvatRectangleD FromLTRB(double left, double top, double right, double bottom) =>
			new CvatRectangleD(left, top, right - left, bottom - top);
}

// Class definitions for XML deserialization