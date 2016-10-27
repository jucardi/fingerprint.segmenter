using System;
using System.Drawing;

namespace Segmenter
{
	public class SegmentInfo
	{
		#region Fields
		private Size  size     = Size.Empty;
		private Point centroid = Point.Empty;
		private float rotation = 0.0f;
		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="SegmentResult"/> class.
		/// </summary>
		/// <param name="size">The image size.</param>
		/// <param name="centroid">The image centroid.</param>
		/// <param name="rotation">The image orientation.</param>
		public SegmentInfo(Size size, Point centroid, float rotation)
		{
			this.size     = size;
			this.centroid = centroid;
			this.rotation = rotation;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the image size.
		/// </summary>
		/// <value>The image size.</value>
		public Size Size
		{
			get { return this.size; }
		}

		/// <summary>
		/// Gets the image centroid position.
		/// </summary>
		/// <value>The centroid position.</value>
		public Point Centroid
		{
			get { return this.centroid; }
		}

		/// <summary>
		/// Gets the image rotation.
		/// </summary>
		/// <value>The image rotation in degrees.</value>
		public float Rotation
		{
			get { return this.rotation; }
		}

		#endregion
	}
}
