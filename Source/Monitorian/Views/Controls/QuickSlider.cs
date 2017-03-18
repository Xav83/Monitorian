﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Monitorian.Views.Controls
{
	public class QuickSlider : Slider
	{
		protected override void OnInitialized(EventArgs e)
		{
			base.OnInitialized(e);

			this.IsSnapToTickEnabled = true;
		}

		private Track _track;
		private Thumb _thumb;
		private bool _canDrag;

		public override void OnApplyTemplate()
		{
			base.OnApplyTemplate();

			_track = this.GetTemplateChild("PART_Track") as Track;
			_thumb = _track?.Thumb;
			_canDrag = FindNonPublicMembers();
			if (!_canDrag)
			{
				// Fallback
				this.IsMoveToPointEnabled = true;
				this.IsManipulationEnabled = true;
			}
		}

		#region Unison

		private static event EventHandler<double> Moved; // Static event

		public bool IsUnison
		{
			get { return (bool)GetValue(IsUnisonProperty); }
			set { SetValue(IsUnisonProperty, value); }
		}
		public static readonly DependencyProperty IsUnisonProperty =
			DependencyProperty.Register(
				"IsUnison",
				typeof(bool),
				typeof(QuickSlider),
				new PropertyMetadata(
					false,
					(d, e) =>
					{
						var instance = (QuickSlider)d;

						if ((bool)e.NewValue)
						{
							Moved += instance.OnMoved;
						}
						else
						{
							Moved -= instance.OnMoved;
						}
					}));

		protected override void OnValueChanged(double oldValue, double newValue)
		{
			base.OnValueChanged(oldValue, newValue);

			if (IsUnison && _canDrag && (_isDragStarting || _thumb.IsDragging))
			{
				Moved?.Invoke(this, newValue - oldValue);
			}
		}

		private void OnMoved(object sender, double e)
		{
			if (ReferenceEquals(this, sender))
				return;

			var brightness = this.Value + e;
			if (brightness < this.Minimum)
			{
				brightness = this.Minimum;
				IsUnison = false;
			}
			else if (this.Maximum < brightness)
			{
				brightness = this.Maximum;
				IsUnison = false;
			}
			this.Value = brightness;
		}

		#endregion

		#region Drag

		private bool _isDragStarting;

		private MethodInfo _updateValue;
		private PropertyInfo _thumbIsDragging;
		private FieldInfo _thumbOriginThumbPoint;
		private FieldInfo _thumbPreviousScreenCoordPosition;
		private FieldInfo _thumbOriginScreenCoordPosition;

		private bool FindNonPublicMembers()
		{
			if (_thumb == null)
				return false;

			// Slider.UpdateValue private method
			_updateValue = typeof(Slider).GetMethod("UpdateValue", BindingFlags.NonPublic | BindingFlags.Instance);
			if (_updateValue == null)
				return false;

			// Thumb.IsDragging public readonly property
			_thumbIsDragging = _thumb.GetType().GetProperty("IsDragging", BindingFlags.Public | BindingFlags.Instance);
			if (_thumbIsDragging == null)
				return false;

			// Thumb._originThumbPoint private field
			_thumbOriginThumbPoint = _thumb.GetType().GetField("_originThumbPoint", BindingFlags.NonPublic | BindingFlags.Instance);
			if (_thumbOriginThumbPoint == null)
				return false;

			// Thumb._previousScreenCoordPosition private field
			_thumbPreviousScreenCoordPosition = _thumb.GetType().GetField("_previousScreenCoordPosition", BindingFlags.NonPublic | BindingFlags.Instance);
			if (_thumbPreviousScreenCoordPosition == null)
				return false;

			// Thumb._originScreenCoordPosition private field
			_thumbOriginScreenCoordPosition = _thumb.GetType().GetField("_originScreenCoordPosition", BindingFlags.NonPublic | BindingFlags.Instance);
			if (_thumbOriginScreenCoordPosition == null)
				return false;

			return true;
		}

		protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
		{
			if (SetValueStartDrag(
				x => e.GetPosition(x),
				x => x.CaptureMouse()))
			{
				e.Handled = true;
			}

			base.OnPreviewMouseDown(e);
		}

		protected override void OnPreviewStylusDown(StylusDownEventArgs e)
		{
			if (SetValueStartDrag(
				x => e.GetPosition(x),
				x => x.CaptureStylus()))
			{
				e.Handled = true;
			}

			base.OnPreviewStylusDown(e);
		}

		protected override void OnPreviewStylusUp(StylusEventArgs e)
		{
			base.OnPreviewStylusUp(e);

			// ReleaseStylusCapture method will release Touch capture as well.
			this.ReleaseStylusCapture();
			VisualStateManager.GoToState(_thumb, "Normal", true);
		}

		// This method will not be called when the event is handled by OnPreviewStylusDown method.
		protected override void OnPreviewTouchDown(TouchEventArgs e)
		{
			if (SetValueStartDrag(
				x => e.GetTouchPoint(x).Position,
				x => x.CaptureTouch(e.TouchDevice)))
			{
				e.Handled = true;
			}

			base.OnPreviewTouchDown(e);
		}

		private bool SetValueStartDrag(
			Func<IInputElement, Point> getPosition,
			Action<UIElement> captureDevice)
		{
			if (!_canDrag)
				return false;

			try
			{
				_isDragStarting = true;

				var originTrackPoint = getPosition(_track);
				var newValue = _track.ValueFromPoint(originTrackPoint);
				newValue = Math.Min(this.Maximum, Math.Max(this.Minimum, Math.Round(newValue)));

				if (newValue == this.Value)
					return false;

				// Set new value.
				_updateValue.Invoke(this, new object[] { newValue });
			}
			finally
			{
				_isDragStarting = false;
			}

			// Reproduce Thumb.OnMouseLeftButtonDown method.
			if (!_thumb.IsDragging)
			{
				// Start drag operation for Thumb.
				_thumb.Focus();
				captureDevice(_thumb);

				_thumbIsDragging.SetValue(_thumb, true);

				var originThumbPoint = getPosition(_thumb);
				var originThumbPointToScreen = _thumb.PointToScreen(originThumbPoint);
				//_thumbOriginThumbPoint.SetValue(_thumb, originThumbPoint);
				_thumbPreviousScreenCoordPosition.SetValue(_thumb, originThumbPointToScreen);
				_thumbOriginScreenCoordPosition.SetValue(_thumb, originThumbPointToScreen);

				try
				{
					_thumb.RaiseEvent(new DragStartedEventArgs(originThumbPoint.X, originThumbPoint.Y));
				}
				catch
				{
					_thumb.CancelDrag();
					throw;
				}
			}
			return true;
		}

		#endregion

		#region Manipulation

		private double _originValue;

		protected override void OnManipulationStarted(ManipulationStartedEventArgs e)
		{
			base.OnManipulationStarted(e);

			_originValue = _track.ValueFromPoint(e.ManipulationOrigin);
		}

		protected override void OnManipulationDelta(ManipulationDeltaEventArgs e)
		{
			base.OnManipulationDelta(e);

			var cumulativeDistance = e.CumulativeManipulation.Translation;
			var cumulativeValue = _track.ValueFromDistance(cumulativeDistance.X, cumulativeDistance.Y);
			var currentValue = _originValue + cumulativeValue;
			currentValue = Math.Min(this.Maximum, Math.Max(this.Minimum, Math.Round(currentValue)));

			if (this.Value == currentValue)
				return;

			this.Value = currentValue;
		}

		#endregion
	}
}