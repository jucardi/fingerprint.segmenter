using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Segmenter
{
	/// <summary>
	/// Fingerprint image segmenter.
	/// </summary>
	public class FingerprintSegmenter
	{
		#region Constants
		private const int DEFAULT_WORKING_SIZE = 200;
		private const int WHITE_THRESHOLD      = 128;
		private const int JACOBI_MAX_ROTATIONS = 50;
		#endregion

		#region Fields
		private int    width         = 0;
		private int    height        = 0;
		private float  scaleFactor   = 0;
		private int    minFilterSize = 1;
		private byte[] buffer        = null;
		private uint   denoiseSteps  = 3;
		private double areaThreshold = 0.4;
		private double sizeThreshold = 0.4;
		private byte[] labelMap      = new byte[256];
		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="FingerprintSegmenter"/> class.
		/// </summary>
		/// <param name="width">The image width.</param>
		/// <param name="height">The image height.</param>
		public FingerprintSegmenter(int width, int height)
		: this(width, height, DEFAULT_WORKING_SIZE)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="FingerprintSegmenter"/> class.
		/// </summary>
		/// <param name="width">The image width.</param>
		/// <param name="height">The image height.</param>
		/// <param name="workingSize">The minimum width/height of the working image.</param>
		public FingerprintSegmenter(int width, int height, int workingSize)
		{
			this.scaleFactor   = Math.Min(width, height) < workingSize ? 1.0f : ((float)Math.Min(width, height) / workingSize);
			this.minFilterSize = Math.Max(1, (int)Math.Ceiling(0.005 * workingSize));
			this.width         = (int)(width / this.scaleFactor);
			this.height        = (int)(height / this.scaleFactor);
			this.buffer        = new byte[this.width * this.height];
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets or sets the denoise filter steps.
		/// </summary>
		/// <value>The denoise filter steps.</value>
		public uint DenoiseSteps
		{
			get { return this.denoiseSteps; }
			set { this.denoiseSteps = value; }
		}

		/// <summary>
		/// Gets or sets the object area threshold.
		/// </summary>
		/// <value>The object area threshold.</value>
		public double AreaThreshold
		{
			get { return this.areaThreshold; }
			set { this.areaThreshold = Math.Max(0.0, value); }
		}

		/// <summary>
		/// Gets or sets the object width/height threshold.
		/// </summary>
		/// <value>The width/height threshold.</value>
		public double SizeThreshold
		{
			get { return this.sizeThreshold; }
			set { this.sizeThreshold = Math.Max(0.0, value); }
		}

		#endregion

		#region Public Methods

		/// <summary>
		/// Extracts the objects from the specified image.
		/// </summary>
		/// <param name="image">The image.</param>
		/// <param name="segments">The fingerprint segments.</param>
		/// <returns><c>true</c> if extractions completes successfully; otherwise, <c>false</c>.</returns>
		public bool Extract(Bitmap image, out SegmentInfo[] segments)
		{
			if (image == null)
				throw new ArgumentNullException("image");

			try
			{
				Monitor.Enter(this);

				byte[] data = GetRawImage(image, this.width, this.height);

				this.ApplyMinFilter(data);

				for (int i = 0; i < this.denoiseSteps; ++i)
					this.ApplyDenoiseFilter(data);

				this.Binarize(data);

				segments = ComputeComponentLabeling(data);
			}
			catch
			{
				segments = new SegmentInfo[0];
				return false;
			}
			finally
			{
				Monitor.Exit(this);
			}

			return true;
		}

		/// <summary>
		/// Extracts the objects from the specified image.
		/// </summary>
		/// <param name="image">The image.</param>
		/// <param name="segments">The fingerprint segments.</param>
		/// <param name="fingerImages">The cropped and rotated segments images.</param>
		/// <returns><c>true</c> if extractions completes successfully; otherwise, <c>false</c>.</returns>
		public bool Extract(Bitmap image, out SegmentInfo[] segments, out Image[] fingerImages)
		{
			if (!Extract(image, out segments))
			{
				fingerImages = new Image[segments.Length];
				return false;
			}

			fingerImages = new Image[segments.Length];

			PointF[] vertices       = new PointF[4];
			Matrix   transformation = new Matrix();
			int[]    limits         = new int[4];

			for (int i = 0; i < segments.Length; ++i)
			{
				SegmentInfo info = segments[i];
				int halfWidth = info.Size.Width /2;
				int halfHeight = info.Size.Height/ 2;

				transformation.Reset();
				transformation.Translate(info.Centroid.X, info.Centroid.Y);
				transformation.Rotate(-info.Rotation);

				vertices[0] = new PointF(-halfWidth, -halfHeight);
				vertices[1] = new PointF(halfWidth, -halfHeight);
				vertices[2] = new PointF(-halfWidth, halfHeight);
				vertices[3] = new PointF(halfWidth, halfHeight);
				limits[0]   = int.MaxValue;
				limits[1]   = 0;
				limits[2]   = int.MaxValue;
				limits[3]   = 0;

				transformation.TransformPoints(vertices);

				for (int j = 0; j < 4; ++j)
				{
					limits[0] = (int)Math.Min(limits[0], vertices[j].X);
					limits[1] = (int)Math.Max(limits[1], vertices[j].X);
					limits[2] = (int)Math.Min(limits[2], vertices[j].Y);
					limits[3] = (int)Math.Max(limits[3], vertices[j].Y);
				}

				int cropWidth  = limits[1] - limits[0];
				int cropHeight = limits[3] - limits[2];
				Bitmap segmentImage = new Bitmap(info.Size.Width, info.Size.Height);

				transformation.Reset();
				transformation.Translate(-cropWidth / 2, -cropHeight / 2);
				transformation.Rotate(info.Rotation, MatrixOrder.Append);
				transformation.Translate(halfWidth, halfHeight, MatrixOrder.Append);

				using (Graphics gfx = Graphics.FromImage(segmentImage))
				{
					gfx.Transform = transformation;
					gfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
					gfx.SmoothingMode = SmoothingMode.HighQuality;
					gfx.DrawImage(image, 0.0f, 0.0f, new Rectangle(limits[0], limits[2], cropWidth, cropHeight), GraphicsUnit.Pixel);
				}

				fingerImages[i] = segmentImage;
			}

			return true;
		}

		#endregion

		#region Private Methods

		/// <summary>
		/// Gets the raw image data.
		/// </summary>
		/// <param name="image">The image.</param>
		/// <returns>The raw image data.</returns>
		private unsafe static byte[] GetRawImage(Bitmap image, int width, int height)
		{
			Bitmap copy = new Bitmap(image, width, height);

			BitmapData data     = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadOnly, copy.PixelFormat);
			byte[]     result   = new byte[data.Width*data.Height];
			int        position = 0;

			try
			{
				switch (data.PixelFormat)
				{
					case PixelFormat.Format8bppIndexed:
					case PixelFormat.Indexed:
					{
						for (int i = 0; i < data.Height; ++i)
						{
							byte* row = (byte*)data.Scan0 + (i * data.Stride);

							for (int j = 0; j < data.Width; ++j)
							{
								result[position++] = row[j];
							}
						}

						break;
					}
					case PixelFormat.Format24bppRgb:
					case PixelFormat.Format32bppRgb:
					case PixelFormat.Format32bppArgb:
					{
						int pixelSize = data.PixelFormat == PixelFormat.Format24bppRgb  ? 3 : 4;
						int offset    = data.PixelFormat == PixelFormat.Format32bppArgb ? 1 : 0;

						for (int i = 0; i < data.Height; ++i)
						{
							byte* row = (byte*)data.Scan0 + (i * data.Stride);

							for (int j = 0; j < data.Width; ++j)
							{
								int rx = j * pixelSize;
								result[position++] = (byte)(0.3f * row[rx + offset] + 0.59f * row[rx + 1 + offset] + 0.11 * row[rx + 2 + offset]);
							}
						}

						break;
					}
					default:
						throw new FormatException("Image pixel format not supported.");
				}
			}
			finally
			{
				copy.UnlockBits(data);
				copy.Dispose();
			}

			return result;
		}

		/// <summary>
		/// Applies the minimum neighborhood filter.
		/// </summary>
		/// <param name="data">The bitmap data.</param>
		private void ApplyMinFilter(byte[] data)
		{
			Array.Copy(data, this.buffer, this.buffer.Length);

			for (int i = 0; i < this.height; ++i)
			{
				int resultLine = i * this.width;

				for (int j = 0; j < this.width; ++j)
				{
					int value = 255;

					for (int ky = -this.minFilterSize; ky <= this.minFilterSize; ++ky)
					{
						int y    = i + ky;
						int line = y*this.width;

						for (int kx = -this.minFilterSize; kx <= this.minFilterSize; ++kx)
						{
							int x = j + kx;

							if (y < 0 || y >= this.height || x < 0 || x >= this.width)
								continue;

							value = Math.Min(value, this.buffer[line + x]);
						}
					}

					data[resultLine + j] = (byte)value;
				}
			}
		}

		/// <summary>
		/// Applies the denoise filter.
		/// </summary>
		/// <param name="data">The bitmap data.</param>
		private void ApplyDenoiseFilter(byte[] data)
		{
			float[,] kernel = new float[,] { { 1.0f / 8, 1.0f / 8, 1.0f / 8 }, { 1.0f / 8, 0.0f, 1.0f / 8 }, { 1.0f / 8, 1.0f / 8, 1.0f / 8 } };

			Array.Copy(data, this.buffer, this.buffer.Length);

			for (int i = 0; i < this.height; ++i)
			{
				int resultLine = i * this.width;

				for (int j = 0; j < this.width; ++j)
				{
					double value = 0.0;

					for (int ky = -1; ky <= 1; ++ky)
					{
						int y = i + ky;
						int line = y * this.width;

						for (int kx = -1; kx <= 1; ++kx)
						{
							int x = j + kx;

							if (y < 0 || y >= this.height || x < 0 || x >= this.width)
							{
								value += 255 * kernel[1 + ky, 1 + kx];
								continue;
							}

							value += kernel[1 + ky, 1 + kx] * this.buffer[line + x];
						}
					}

					data[resultLine + j] = (byte)Math.Ceiling(value);
				}
			}
		}

		/// <summary>
		/// Binarize the image using Otsu's method.
		/// </summary>
		/// <param name="data">The bitmap data.</param>
		private void Binarize(byte[] data)
		{
			double[] histogram = new double[256];
			double[] sigma     = new double[256];
			double   N         = this.width * this.height;
			double   p1, p2, p12;

			// Compute histogram
			for (int i = 0; i < data.Length; ++i)
				histogram[data[i]] += 1.0;

			for (int i = 0; i < 256; ++i)
				histogram[i] /= N;

			for (int k = 1; k < 256; ++k)
			{
				p1 = ComputeCumulative(0, k, histogram);
				p2 = ComputeCumulative(k + 1, 255, histogram);
				p12 = p1 * p2;

				if (p12 == 0)
					p12 = 1;

				double diff = (ComputeMean(0, k, histogram) * p2) - (ComputeMean(k + 1, 255, histogram) * p1);
				sigma[k] = (float)diff * diff / p12;
			}

			double maximum   = 0;
			double threshold = 0;

			for (int i = 0; i < sigma.Length; i++)
			{
				if (sigma[i] <= maximum)
					continue;

				maximum   = sigma[i];
				threshold = i;
			}

			threshold = 1.2 * threshold;

			// Binarize
			for (int i = 0; i < data.Length; ++i)
				data[i] = data[i] >= threshold ? (byte)255 : (byte)0;
		}

		/// <summary>
		/// Computes the cumulative histogram.
		/// </summary>
		/// <param name="init">The init position.</param>
		/// <param name="end">The end position.</param>
		/// <param name="histogram">The histogram.</param>
		/// <returns>The cumulative value.</returns>
		private double ComputeCumulative(int init, int end, double[] histogram)
		{
			double sum = 0;

			for (int i = init; i <= end; ++i)
				sum += histogram[i];

			return sum;
		}

		/// <summary>
		/// Computes the mean histogram.
		/// </summary>
		/// <param name="init">The init position.</param>
		/// <param name="end">The end position.</param>
		/// <param name="histogram">The histogram.</param>
		/// <returns>The mean value.</returns>
		private double ComputeMean(int init, int end, double[] histogram)
		{
			double sum = 0;

			for (int i = init; i <= end; ++i)
				sum += i * histogram[i];

			return sum;
		}

		/// <summary>
		/// Computes the connected component labeling.
		/// </summary>
		/// <param name="data">The bitmap data.</param>
		/// <returns>The list of objects segments.</returns>
		private unsafe SegmentInfo[] ComputeComponentLabeling(byte[] data)
		{
			int labelsCount  = 0;
			int objectsCount = 0;
			int position     = 0;

			for (byte i = 0; i != 255; ++i)
				labelMap[i] = i;

			this.buffer = new byte[data.Length];

			// 1 - for pixels of the first row
			if (data[0] < WHITE_THRESHOLD)
			{
				this.buffer[position] = (byte)++labelsCount;
			}

			++position;

			// process the rest of the first row
			for (int x = 1; x < this.width; ++x, ++position)
			{
				// check if we need to label current pixel
				if (data[position] < WHITE_THRESHOLD)
				{
					// check if the previous pixel already was labeled
					if (data[position - 1] < WHITE_THRESHOLD)
					{
						// label current pixel, as the previous
						this.buffer[position] = this.buffer[position - 1];
					}
					else
					{
						// create new label
						this.buffer[position] = (byte)++labelsCount;

						if (labelsCount > 255)
							return null;
					}
				}
			}

			// 2 - for other rows
			// for each row
			for (int y = 1; y < this.height; ++y)
			{
				// for the first pixel of the row, we need to check
				// only upper and upper-right pixels
				if (data[position] < WHITE_THRESHOLD)
				{
					// check surrounding pixels
					if (data[position - this.width] < WHITE_THRESHOLD)
					{
						// label current pixel, as the above
						this.buffer[position] = this.buffer[position - this.width];
					}
					else if (data[position + 1 - this.width] < WHITE_THRESHOLD)
					{
						// label current pixel, as the above right
						this.buffer[position] = this.buffer[position + 1 - this.width];
					}
					else
					{
						// create new label
						this.buffer[position] = (byte)++labelsCount;

						if (labelsCount > 255)
							return null;
					}
				}

				++position;

				// check left pixel and three upper pixels for the rest of pixels
				for (int x = 1; x < this.width - 1; ++x, ++position)
				{
					if (data[position] < WHITE_THRESHOLD)
					{
						// check surrounding pixels
						if (data[position - 1] < WHITE_THRESHOLD)
						{
							// label current pixel, as the left
							this.buffer[position] = this.buffer[position - 1];
						}
						else if (data[position - 1 - this.width] < WHITE_THRESHOLD)
						{
							// label current pixel, as the above left
							this.buffer[position] = this.buffer[position - 1 - this.width];
						}
						else if (data[position - this.width] < WHITE_THRESHOLD)
						{
							// label current pixel, as the above
							this.buffer[position] = this.buffer[position - this.width];
						}

						if (data[position + 1 - this.width] < WHITE_THRESHOLD)
						{
							if (this.buffer[position] == 0)
							{
								// label current pixel, as the above right
								this.buffer[position] = this.buffer[position + 1 - this.width];
							}
							else
							{
								int l1 = this.buffer[position];
								int l2 = this.buffer[position + 1 - this.width];

								if ((l1 != l2) && (labelMap[l1] != labelMap[l2]))
								{
									// merge
									if (labelMap[l1] == l1)
									{
										// map left value to the right
										labelMap[l1] = labelMap[l2];
									}
									else if (labelMap[l2] == l2)
									{
										// map right value to the left
										labelMap[l2] = labelMap[l1];
									}
									else
									{
										// both values already mapped
										labelMap[labelMap[l1]] = labelMap[l2];
										labelMap[l1] = labelMap[l2];
									}

									// reindex
									for (int i = 1; i <= labelsCount; ++i)
									{
										if (labelMap[i] != i)
										{
											// reindex
											byte j = labelMap[i];
											while (j != labelMap[j])
											{
												j = labelMap[j];
											}
											labelMap[i] = j;
										}
									}
								}
							}
						}

						// label the object if it is not yet
						if (this.buffer[position] == 0)
						{
							// create new label
							this.buffer[position] = (byte)++labelsCount;

							if (labelsCount > 255)
								return null;
						}
					}
				}

				// for the last pixel of the row, we need to check
				// only upper and upper-left pixels
				if (data[position] < WHITE_THRESHOLD)
				{
					// check surrounding pixels
					if (data[position - 1] < WHITE_THRESHOLD)
					{
						// label current pixel, as the left
						this.buffer[position] = this.buffer[position - 1];
					}
					else if (data[position - 1 - this.width] < WHITE_THRESHOLD)
					{
						// label current pixel, as the above left
						this.buffer[position] = this.buffer[position - 1 - this.width];
					}
					else if (data[position - this.width] < WHITE_THRESHOLD)
					{
						// label current pixel, as the above
						this.buffer[position] = this.buffer[position - this.width];
					}
					else
					{
						// create new label
						this.buffer[position] = (byte)++labelsCount;

						if (labelsCount > 255)
							return null;
					}
				}

				++position;
			}

			// allocate remapping array
			int[] reMap = new int[labelMap.Length];

			// count objects and prepare remapping array
			objectsCount = 0;

			for (int i = 1; i <= labelsCount; ++i)
			{
				if (labelMap[i] == i)
				{
					// increase objects count
					reMap[i] = ++objectsCount;
				}
			}

			// second pass to complete remapping
			for (int i = 1; i <= labelsCount; ++i)
			{
				if (labelMap[i] != i)
				{
					reMap[i] = reMap[labelMap[i]];
				}
			}

			// repair object labels
			for (int i = 0, n = this.buffer.Length; i < n; ++i)
			{
				this.buffer[i] = (byte)reMap[this.buffer[i]];
			}

			return this.CollectObjects(objectsCount, this.buffer);
		}

		/// <summary>
		/// Collects the labeling objects.
		/// </summary>
		/// <param name="count">The objects count.</param>
		/// <param name="map">The objects labels map.</param>
		/// <returns>The list of objects segments.</returns>
		private SegmentInfo[] CollectObjects(int count, byte[] map)
		{
			int  label    = 0;
			int  position = 0;

			int[]  x1 = new int[count + 1];
			int[]  y1 = new int[count + 1];
			int[]  x2 = new int[count + 1];
			int[]  y2 = new int[count + 1];
			long[] cx = new long[count + 1];
			long[] cy = new long[count + 1];
			long[] area = new long[count + 1];

			for (int j = 1; j <= count; ++j)
			{
				x1[j] = this.width;
				y1[j] = this.height;
			}

			for (int y = 0; y < this.height; ++y)
			{
				for (int x = 0; x < this.width; ++x, ++position)
				{
					label = map[position];

					if (label == 0)
						continue;

					x1[label] = x < x1[label] ? x : x1[label];
					x2[label] = x > x2[label] ? x : x2[label];
					y1[label] = y < y1[label] ? y : y1[label];
					y2[label] = y > y2[label] ? y : y2[label];

					cx[label] += x;
					cy[label] += y;
					area[label]++;
				}
			}

			double absoluteAreaThrehold   = 0;
			double absoluteWidthThrehold  = 0;
			double absoluteHeightThrehold = 0;

			for(int i = 1; i <= count; ++i)
			{
				absoluteAreaThrehold   = Math.Max(absoluteAreaThrehold, area[i]);
				absoluteWidthThrehold  = Math.Max(absoluteWidthThrehold, x2[i] - x1[i]);
				absoluteHeightThrehold = Math.Max(absoluteHeightThrehold, y2[i] - y1[i]);

				cx[i] /= area[i];
				cy[i] /= area[i];
			}

			absoluteAreaThrehold   = this.areaThreshold * absoluteAreaThrehold;
			absoluteWidthThrehold  = this.sizeThreshold * absoluteWidthThrehold;
			absoluteHeightThrehold = this.sizeThreshold * absoluteHeightThrehold;

			List<SegmentInfo> result = new List<SegmentInfo>();

			for (int j = 1; j <= count; ++j)
			{
				if (area[j] < absoluteAreaThrehold || (x2[j] - x1[j]) < (absoluteWidthThrehold) || (y2[j] - y1[j]) < absoluteHeightThrehold)
					continue;

				SegmentInfo info = ComputeSegmentInfo(j, map, x1[j], x2[j], y1[j], y2[j], (int)cx[j], (int)cy[j]);

				if(info != null)
					result.Add(info);
			}

			return result.ToArray();
		}

		/// <summary>
		/// Computes the segment information.
		/// </summary>
		/// <param name="label">The object label.</param>
		/// <param name="map">The objects labels map.</param>
		/// <param name="x1">The minimum x-coordinate.</param>
		/// <param name="x2">The maximum x-coordinate.</param>
		/// <param name="y1">The minimum y-coordinate.</param>
		/// <param name="y2">The maximum x-coordinate.</param>
		/// <param name="cx">The x-center.</param>
		/// <param name="cy">The y-center.</param>
		/// <param name="area">The object area.</param>
		/// <returns>The segment information.</returns>
		private SegmentInfo ComputeSegmentInfo(int label, byte[] map, int x1, int x2, int y1, int y2, int cx, int cy)
		{
			double[,] matrix = new double[3, 3];
			double[,] eigenvectors = null;
			double[] eigenvalues = null;
			int area = 0;

			// Compute covariance matrix. Only border point are taking in account.
			for (int m = y1; m <= y2; ++m)
			{
				int line = m * this.width;
				int left = -1;

				for (int n = x1; n <= x2; ++n)
				{
					if (map[line + n] != label)
						continue;

					double coffx = (n - cx);
					double coffy = (m - cy);

					matrix[0, 0] += coffx * coffx;
					matrix[1, 0] += coffy * coffx;
					matrix[0, 1] += coffx * coffy;
					matrix[1, 1] += coffy * coffy;

					left = n;
					++area;
					break;
				}

				if(left < 0)
					continue;

				for (int n = x2; n > left; --n)
				{
					if (map[line + n] != label)
						continue;

					double coffx = (n - cx);
					double coffy = (m - cy);

					matrix[0, 0] += coffx * coffx;
					matrix[1, 0] += coffy * coffx;
					matrix[0, 1] += coffx * coffy;
					matrix[1, 1] += coffy * coffy;

					++area;
					break;
				}
			}

			matrix[0, 0] /= area;
			matrix[1, 0] /= area;
			matrix[0, 1] /= area;
			matrix[1, 1] /= area;

			// Compute principal axis from the object point set.
			if (!ComputeJacobi(matrix, out eigenvalues, out eigenvectors))
				return null;

			// Get the best x' vector.
			if (Math.Abs(eigenvectors[0, 0]) < Math.Abs(eigenvectors[1, 0]))
			{
				eigenvectors[0, 0] = eigenvectors[1, 0];
				eigenvectors[0, 1] = eigenvectors[1, 1];
			}

			if (eigenvectors[0, 0] < 0.0)
			{
				eigenvectors[0, 0] = -eigenvectors[0, 0];
				eigenvectors[0, 1] = -eigenvectors[0, 1];
			}

			double cos = eigenvectors[0, 0];
			double sin = eigenvectors[0, 1];
			double angle = Math.Atan2(sin, cos);
			double delta = 0.0;
			double xp1 = double.MaxValue;
			double yp1 = double.MaxValue;
			double xp2 = 0.0;
			double yp2 = 0.0;
			double wp = 0.0;
			double hp = 0.0;

			ComputeBox(label, map, x1, x2, y1, y2, cx, cy, cos, sin, ref xp1, ref xp2, ref yp1, ref yp2);

			wp = xp2 - xp1;
			hp = yp2 - yp1;

			// Principal axis are not really related to minimum bounding box axis. 
			// With dense cluster points the principal axis are near to the actual axis and the diagonal of the box.
			// Rotating Calipers is no optimal when the polygon have too many sides.
			for (int beta = 5; beta < 45; beta += 5)
			{
				delta = Math.PI * beta / 180.0;
				cos = Math.Cos(angle + delta);
				sin = Math.Sin(angle + delta);

				ComputeBox(label, map, x1, x2, y1, y2, cx, cy, cos, sin, ref xp1, ref xp2, ref yp1, ref yp2);

				if (wp * hp > (xp2 - xp1) * (yp2 - yp1))
				{
					wp = xp2 - xp1;
					hp = yp2 - yp1;
					angle = angle + delta;
					beta = 5;
				}
			}

			if (wp > hp)
			{
				double swap = wp;
				wp = hp;
				hp = swap;
				angle += Math.PI / 2;
			}

			if (angle > Math.PI/2)
				angle -= Math.PI;
			else if (angle < -Math.PI/2)
				angle += Math.PI;

			Size size = new Size((int)(1.12 * this.scaleFactor * wp), (int)(1.12 * this.scaleFactor * hp));
			Point centroid = new Point((int)(this.scaleFactor * cx), (int)(this.scaleFactor * cy));

			return new SegmentInfo(size, centroid, (float)(180.0 * angle / Math.PI));
		}

		/// <summary>
		/// Computes the object box limits in the rotated coordinate system.
		/// </summary>
		/// <param name="label">The object label.</param>
		/// <param name="map">The labels map.</param>
		/// <param name="x1">The minimum x-coordiate of the label.</param>
		/// <param name="x2">The maximum x-coordiate of the label.</param>
		/// <param name="y1">The minimum y-coordiate of the label.</param>
		/// <param name="y2">The maximum x-coordiate of the label.</param>
		/// <param name="cx">The center x-coordiate of the label.</param>
		/// <param name="cy">The center y-coordiate of the label.</param>
		/// <param name="cos">The cosine of the angle of rotation.</param>
		/// <param name="sin">The sine of the angle of rotation.</param>
		/// <param name="xp1">The minimum x-coordiate in the rotated system.</param>
		/// <param name="xp2">The maximum x-coordiate in the rotated system.</param>
		/// <param name="yp1">The minimum y-coordiate in the rotated system.</param>
		/// <param name="yp2">The maximum y-coordiate in the rotated system.</param>
		private void ComputeBox(int label, byte[] map, int x1, int x2, int y1, int y2, int cx, int cy, double cos, double sin, ref double xp1, ref double xp2, ref double yp1, ref double yp2)
		{
			xp1 = double.MaxValue;
			yp1 = double.MaxValue;
			xp2 = 0.0;
			yp2 = 0.0;

			for (int m = y1; m <= y2; ++m)
			{
				int line = m * this.width;

				for (int n = x1; n <= x2; ++n)
				{
					if (map[line + n] != label)
						continue;

					double xp = (n - cx) * cos - (m - cy) * sin;
					double yp = (n - cx) * sin + (m - cy) * cos;

					xp1 = xp < xp1 ? xp : xp1;
					xp2 = xp > xp2 ? xp : xp2;
					yp1 = yp < yp1 ? yp : yp1;
					yp2 = yp > yp2 ? yp : yp2;
				}
			}
		}

		/// <summary>
		/// Computes the eigenvalues and eigenvectors of a 3x3 matrix using the Jabobi method.
		/// </summary>
		/// <see>Numerical Recipes 11.1</see>
		/// <remarks>For 3x3 matrices is not necesary implement a more complicated algoritmh like QR or Householder. http://beige.ucs.indiana.edu/B673/node22.html.</remarks>
		/// <remarks>The eigenvalues and eigenvectors are normalized. The content of the matrix is destroyed.</remarks>
		/// <param name="matrix">The 3x3 matrix.</param>
		/// <param name="eigenvalues">The 3 eigenvalues.</param>
		/// <param name="eigenvectors">The 3x3 eigenvectors matrix.</param>
		/// <returns><c>true</c> if extractions completes successfully; otherwise, <c>false</c>.</returns>
		private static bool ComputeJacobi(double[,] matrix, out double[] eigenvalues, out double[,] eigenvectors)
		{
			eigenvalues  = new double[3];
			eigenvectors = new double[3,3];

			if(matrix == null)
				throw new ArgumentNullException("matrix");

			if(matrix.GetLength(0) != 3 || matrix.GetLength(1) != 3)
				throw new ArgumentException("The matrix is not 3x3.");

			int i, j, k, iq, ip, numPos;
			double tresh, theta, tau, t, sm, s, h, g, c, tmp;
			double[] b = new double[3];
			double[] z = new double[3] { 0.0, 0.0, 0.0 };

			eigenvectors[0,1] = eigenvectors[0,2] = eigenvectors[1,0] = eigenvectors[1,2] = eigenvectors[2,0] = eigenvectors[2,1] = 0.0;
			eigenvectors[0,0] = eigenvectors[1,1] = eigenvectors[2,2] = 1.0;

			b[0] = eigenvalues[0] = matrix[0,0];
			b[1] = eigenvalues[1] = matrix[1,1];
			b[2] = eigenvalues[2] = matrix[2,2];

			for(i=0; i < JACOBI_MAX_ROTATIONS; i++) 
			{
				sm = 0.0;

				for (ip = 0; ip < 2; ip++) 
				{
					for (iq = ip+1; iq < 3; iq++)
					{
						sm += Math.Abs(matrix[ip,iq]);
					}
				}

				if(sm == 0.0)
					break;

				if(i < 3)
					tresh = 0.2*sm/9.0;
				else
					tresh = 0.0;

				for(ip = 0; ip < 2; ip++) 
				{
					for(iq = ip+1; iq < 3; iq++) 
					{
						g = 100.0 * Math.Abs(matrix[ip,iq]);

						if ((i > 3) && (Math.Abs(eigenvalues[ip]) + g) == Math.Abs(eigenvalues[ip]) && (Math.Abs(eigenvalues[iq]) + g) == Math.Abs(eigenvalues[iq]))
						{
							matrix[ip,iq] = 0.0;
						}
						else if (Math.Abs(matrix[ip,iq]) > tresh) 
						{
							h = eigenvalues[iq] - eigenvalues[ip];

							if((Math.Abs(h)+g) == Math.Abs(h))
							{
								t = (matrix[ip,iq]) / h;
							}
							else 
							{
								theta = 0.5f*h / (matrix[ip,iq]);
								t = 1.0 / (Math.Abs(theta) + Math.Sqrt(1.0+theta*theta));
								if(theta < 0.0)
								{
									t = -t;
								}
							}

							c = 1.0 / Math.Sqrt(1+t*t);
							s = t*c;
							tau = s/(1.0+c);
							h = t*matrix[ip,iq];
							z[ip] -= h;
							z[iq] += h;
							eigenvalues[ip] -= h;
							eigenvalues[iq] += h;
							matrix[ip,iq]=0.0;

							for (j = 0; j <= ip-1; j++) 
							{
								DoJacobiRotation(matrix, j, ip, j, iq, s, g, h, tau);
							}

							for (j = ip+1; j <= iq-1; j++) 
							{
								DoJacobiRotation(matrix, ip, j, j, iq, s, g, h, tau);
							}

							for (j = iq+1; j < 3; j++) 
							{
								DoJacobiRotation(matrix, ip, j, iq, j, s, g, h, tau);
							}
							for (j = 0; j < 3; j++) 
							{
								DoJacobiRotation(eigenvectors, j, ip, j, iq, s, g, h, tau);
							}
						}
					}
				}

				for (ip = 0; ip < 3; ip++) 
				{
					b[ip] += z[ip];
					eigenvalues[ip] = b[ip];
					z[ip] = 0.0;
				}
			}

			if (i >= JACOBI_MAX_ROTATIONS)
				return false;

			// Sort eigenvectors from the biggest eigenvalue to the lowest eigenvalue.
			for(j = 0; j < 2; j++)
			{
				k = j;
				tmp = eigenvalues[k];

				for(i = j+1; i < 3; i++)
				{
					if (eigenvalues[i] >= tmp)
					{
						k = i;
						tmp = eigenvalues[k];
					}
				}
		    
				if(k != j) 
				{
					eigenvalues[k] = eigenvalues[j];
					eigenvalues[j] = tmp;

					for(i = 0; i < 3; i++)
					{
						double swap = eigenvectors[i,j];
						eigenvectors[i,j] = eigenvectors[i,k];
						eigenvectors[i,k] = swap;
					}
				}
			}

			// Check eigenvectors consistency.
			for(j = 0; j < 3; j++)
			{
				for(numPos = 0, i=0; i < 3; i++)
					if (eigenvectors[i,j] >= 0.0)
						numPos++;

				if(numPos < 2)
					for(i = 0; i < 3; i++)
						eigenvectors[i,j] *= -1.0;
			}

			return true;
		}

		/// <summary>
		/// Do a Jacobi's rotation.
		/// </summary>
		/// <param name="matrix">the matrix.</param>
		/// <param name="i">The i index.</param>
		/// <param name="j">The j index.</param>
		/// <param name="k">The k index.</param>
		/// <param name="l">The l index.</param>
		/// <param name="s">The s parameter.</param>
		/// <param name="g">The g parameter.</param>
		/// <param name="h">The h parameter.</param>
		/// <param name="tau">The tau parameter.</param>
		private static void DoJacobiRotation(double[,] matrix, int i, int j, int k, int l, double s, double g, double h, double tau)
		{
			g = matrix[i, j];
			h = matrix[k, l];
			matrix[i, j] = g - s * (h + g * tau);
			matrix[k, l] = h + s * (g - h * tau);
		}

		#endregion
	}
}
