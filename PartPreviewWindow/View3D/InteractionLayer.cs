﻿/*
Copyright (c) 2017, Lars Brubaker, John Lewin
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies,
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MatterHackers.Agg;
using MatterHackers.Agg.Image;
using MatterHackers.Agg.UI;
using MatterHackers.MeshVisualizer;
using MatterHackers.PolygonMesh;
using MatterHackers.RayTracer;
using MatterHackers.RayTracer.Traceable;
using MatterHackers.RenderOpenGl;
using MatterHackers.RenderOpenGl.OpenGl;
using MatterHackers.VectorMath;
using static MatterHackers.MeshVisualizer.MeshViewerWidget;

namespace MatterHackers.MatterControl.PartPreviewWindow
{
	public class LightingData
	{
		internal float[] ambientLight = { 0.2f, 0.2f, 0.2f, 1.0f };

		internal float[] diffuseLight0 = { 0.7f, 0.7f, 0.7f, 1.0f };
		internal float[] specularLight0 = { 0.5f, 0.5f, 0.5f, 1.0f };
		internal float[] lightDirection0 = { -1, -1, 1, 0.0f };

		internal float[] diffuseLight1 = { 0.5f, 0.5f, 0.5f, 1.0f };
		internal float[] specularLight1 = { 0.3f, 0.3f, 0.3f, 1.0f };
		internal float[] lightDirection1 = { 1, 1, 1, 0.0f };
	}

	public class TumbleCubeControl : GuiWidget
	{
		LightingData lighting = new LightingData();
		Mesh cube = PlatonicSolids.CreateCube(3, 3, 3);
		IPrimitive cubeTraceData;
		InteractionLayer interactionLayer;
		WorldView cubeWorld;

		public TumbleCubeControl(InteractionLayer interactionLayer)
			: base(100, 100)
		{
			this.interactionLayer = interactionLayer;

			TextureFace(cube.Faces[0], "Top");
			TextureFace(cube.Faces[1], "Left", Matrix4X4.CreateRotationZ(MathHelper.Tau / 4));
			TextureFace(cube.Faces[2], "Right", Matrix4X4.CreateRotationZ(-MathHelper.Tau/4));
			TextureFace(cube.Faces[3], "Bottom", Matrix4X4.CreateRotationZ(MathHelper.Tau / 2));
			TextureFace(cube.Faces[4], "Back", Matrix4X4.CreateRotationZ(MathHelper.Tau / 2));
			TextureFace(cube.Faces[5], "Front");

			cubeTraceData = cube.CreateTraceData();
		}

		public override void OnDraw(Graphics2D graphics2D)
		{
			var screenSpcaeBounds = this.TransformToScreenSpace(LocalBounds);
			cubeWorld = new WorldView(screenSpcaeBounds.Width, screenSpcaeBounds.Height);

			var forward = -Vector3.UnitZ;
			var directionForward = Vector3.TransformNormal(forward, interactionLayer.World.InverseModelviewMatrix);

			var up = Vector3.UnitY;
			var directionUp = Vector3.TransformNormal(up, interactionLayer.World.InverseModelviewMatrix);
			cubeWorld.RotationMatrix = Matrix4X4.LookAt(Vector3.Zero, directionForward, directionUp);

			InteractionLayer.SetGlContext(cubeWorld, screenSpcaeBounds, lighting);
			GLHelper.Render(cube, Color.White, Matrix4X4.Identity, RenderTypes.Shaded);
			InteractionLayer.UnsetGlContext();

			base.OnDraw(graphics2D);
		}

		public override void OnMouseDown(MouseEventArgs mouseEvent)
		{
			base.OnMouseDown(mouseEvent);

			Ray ray = cubeWorld.GetRayForLocalBounds(mouseEvent.Position);
			IntersectInfo info = cubeTraceData.GetClosestIntersection(ray);

			if (info != null)
			{
				var normal = ((TriangleShape)info.closestHitObject).Plane.PlaneNormal;
				var directionForward = -new Vector3(normal);

				var directionUp = Vector3.UnitY;
				if (directionForward.Equals(Vector3.UnitX, .001))
				{
					directionUp = Vector3.UnitZ;
				}
				else if (directionForward.Equals(-Vector3.UnitX, .001))
				{
					directionUp = Vector3.UnitZ;
				}
				else if (directionForward.Equals(Vector3.UnitY, .001))
				{
					directionUp = Vector3.UnitZ;
				}
				else if (directionForward.Equals(-Vector3.UnitY, .001))
				{
					directionUp = Vector3.UnitZ;
				}
				else if (directionForward.Equals(Vector3.UnitZ, .001))
				{
					directionUp = -Vector3.UnitY;
				}

				var look = Matrix4X4.LookAt(Vector3.Zero, directionForward, directionUp);

				var start = new Quaternion(interactionLayer.World.RotationMatrix);
				var end = new Quaternion(look);

				Task.Run(() =>
				{
					double duration = .25;
					var timer = Stopwatch.StartNew();
					var time = timer.Elapsed.TotalSeconds;
					while (time < duration)
					{
						var current = Quaternion.Slerp(start, end, time / duration);
						UiThread.RunOnIdle(() =>
						{
							interactionLayer.World.RotationMatrix = Matrix4X4.CreateRotation(current);
							Invalidate();
						});
						time = timer.Elapsed.TotalSeconds;
						Thread.Sleep(10);
					}
					interactionLayer.World.RotationMatrix = Matrix4X4.CreateRotation(end);
					Invalidate();
				});
			}
		}

		public override void OnMouseMove(MouseEventArgs mouseEvent)
		{
			// find the ray for this control
			// check what face it hits
			// mark that face to draw a highlight
			base.OnMouseMove(mouseEvent);
		}

		public override void OnMouseUp(MouseEventArgs mouseEvent)
		{
			base.OnMouseUp(mouseEvent);

			interactionLayer.Focus();
		}

		private static void TextureFace(Face face, string name, Matrix4X4? initialRotation = null)
		{
			ImageBuffer textureToUse = new ImageBuffer(256, 256);
			var frontGraphics = textureToUse.NewGraphics2D();
			frontGraphics.Clear(Color.White);
			frontGraphics.DrawString(name,
				textureToUse.Width / 2,
				textureToUse.Height / 2,
				60,
				justification: Agg.Font.Justification.Center,
				baseline: Agg.Font.Baseline.BoundsCenter);
			MeshHelper.PlaceTextureOnFace(face, textureToUse, MeshHelper.GetMaxFaceProjection(face, textureToUse, initialRotation));
		}
	}

	public class InteractionLayer : GuiWidget, IInteractionVolumeContext
	{
		private int volumeIndexWithMouseDown = -1;

		public WorldView World { get; }

		public InteractiveScene Scene { get; }

		public event EventHandler<DrawEventArgs> DrawGlOpaqueContent;
		public event EventHandler<DrawEventArgs> DrawGlTransparentContent;

		public bool DoOpenGlDrawing { get; set; } = true;

		// TODO: Collapse into auto-property
		private List<InteractionVolume> interactionVolumes = new List<InteractionVolume>();
		public List<InteractionVolume> InteractionVolumes { get; }

		private UndoBuffer undoBuffer;

		private Action notifyPartChanged;

		private LightingData lighting = new LightingData();

		public InteractionLayer(WorldView world, UndoBuffer undoBuffer, InteractiveScene scene)
		{
			this.Scene = scene;
			this.World = world;
			this.InteractionVolumes = interactionVolumes;
			this.undoBuffer = undoBuffer;
			this.notifyPartChanged = notifyPartChanged;

			var labelContainer = new GuiWidget();
			labelContainer.Selectable = false;
			labelContainer.AnchorAll();
			this.AddChild(labelContainer);
		}

		public override void OnLoad(EventArgs args)
		{
			this.AddChild(new TumbleCubeControl(this)
			{
				Margin = new BorderDouble(50, 0, 0, 50),
				VAnchor = VAnchor.Top,
				HAnchor = HAnchor.Left,
			});

			base.OnLoad(args);
		}

		internal void SetRenderTarget(GuiWidget renderSource)
		{
			renderSource.AfterDraw += RenderSource_DrawExtra;
		}

		private void RenderSource_DrawExtra(object sender, DrawEventArgs e)
		{
			if (DoOpenGlDrawing)
			{
				SetGlContext(this.World, this.TransformToScreenSpace(LocalBounds), lighting);
				OnDrawGlContent(e);
				UnsetGlContext();
			}
		}

		public override void OnMouseDown(MouseEventArgs mouseEvent)
		{
			base.OnMouseDown(mouseEvent);

			int volumeHitIndex;
			Ray ray = this.World.GetRayForLocalBounds(mouseEvent.Position);
			IntersectInfo info;
			if (this.Scene.HasSelection
				&& !SuppressUiVolumes
				&& FindInteractionVolumeHit(ray, out volumeHitIndex, out info))
			{
				MouseEvent3DArgs mouseEvent3D = new MouseEvent3DArgs(mouseEvent, ray, info);
				volumeIndexWithMouseDown = volumeHitIndex;
				interactionVolumes[volumeHitIndex].OnMouseDown(mouseEvent3D);
				SelectedInteractionVolume = interactionVolumes[volumeHitIndex];
			}
			else
			{
				SelectedInteractionVolume = null;
			}
		}

		public override void OnMouseMove(MouseEventArgs mouseEvent)
		{
			base.OnMouseMove(mouseEvent);

			if (SuppressUiVolumes 
				|| !this.PositionWithinLocalBounds(mouseEvent.X, mouseEvent.Y))
			{
				return;
			}

			Ray ray = this.World.GetRayForLocalBounds(mouseEvent.Position);
			IntersectInfo info = null;
			if (MouseDownOnInteractionVolume && volumeIndexWithMouseDown != -1)
			{
				MouseEvent3DArgs mouseEvent3D = new MouseEvent3DArgs(mouseEvent, ray, info);
				interactionVolumes[volumeIndexWithMouseDown].OnMouseMove(mouseEvent3D);
			}
			else
			{
				MouseEvent3DArgs mouseEvent3D = new MouseEvent3DArgs(mouseEvent, ray, info);

				int volumeHitIndex;
				FindInteractionVolumeHit(ray, out volumeHitIndex, out info);

				for (int i = 0; i < interactionVolumes.Count; i++)
				{
					if (i == volumeHitIndex)
					{
						interactionVolumes[i].MouseOver = true;
						interactionVolumes[i].MouseMoveInfo = info;

						HoveredInteractionVolume = interactionVolumes[i];
					}
					else
					{
						interactionVolumes[i].MouseOver = false;
						interactionVolumes[i].MouseMoveInfo = null;
					}

					interactionVolumes[i].OnMouseMove(mouseEvent3D);
				}
			}
		}

		public override void OnMouseUp(MouseEventArgs mouseEvent)
		{
			Invalidate();

			if (SuppressUiVolumes)
			{
				return;
			}

			int volumeHitIndex;
			Ray ray = this.World.GetRayForLocalBounds(mouseEvent.Position);
			IntersectInfo info;
			bool anyInteractionVolumeHit = FindInteractionVolumeHit(ray, out volumeHitIndex, out info);
			MouseEvent3DArgs mouseEvent3D = new MouseEvent3DArgs(mouseEvent, ray, info);

			if (MouseDownOnInteractionVolume && volumeIndexWithMouseDown != -1)
			{
				interactionVolumes[volumeIndexWithMouseDown].OnMouseUp(mouseEvent3D);
				SelectedInteractionVolume = null;

				volumeIndexWithMouseDown = -1;
			}
			else
			{
				volumeIndexWithMouseDown = -1;

				if (anyInteractionVolumeHit)
				{
					interactionVolumes[volumeHitIndex].OnMouseUp(mouseEvent3D);
				}
				SelectedInteractionVolume = null;
			}

			base.OnMouseUp(mouseEvent);
		}

		private bool FindInteractionVolumeHit(Ray ray, out int interactionVolumeHitIndex, out IntersectInfo info)
		{
			interactionVolumeHitIndex = -1;
			if (interactionVolumes.Count == 0 || interactionVolumes[0].CollisionVolume == null)
			{
				info = null;
				return false;
			}

			// TODO: Rewrite as projection without extra list
			List<IPrimitive> uiTraceables = new List<IPrimitive>();
			foreach (InteractionVolume interactionVolume in interactionVolumes)
			{
				if (interactionVolume.CollisionVolume != null)
				{
					IPrimitive traceData = interactionVolume.CollisionVolume;
					uiTraceables.Add(new Transform(traceData, interactionVolume.TotalTransform));
				}
			}

			IPrimitive allUiObjects = BoundingVolumeHierarchy.CreateNewHierachy(uiTraceables);

			info = allUiObjects.GetClosestIntersection(ray);
			if (info != null)
			{
				for (int i = 0; i < interactionVolumes.Count; i++)
				{
					List<IBvhItem> insideBounds = new List<IBvhItem>();
					if (interactionVolumes[i].CollisionVolume != null)
					{
						interactionVolumes[i].CollisionVolume.GetContained(insideBounds, info.closestHitObject.GetAxisAlignedBoundingBox());
						if (insideBounds.Contains(info.closestHitObject))
						{
							interactionVolumeHitIndex = i;
							return true;
						}
					}
				}
			}

			return false;
		}

		public void AddTransformSnapshot(Matrix4X4 originalTransform)
		{
			if (this.Scene.HasSelection && this.Scene.SelectedItem.Matrix != originalTransform)
			{
				this.undoBuffer.Add(new TransformUndoCommand(Scene.SelectedItem, originalTransform, Scene.SelectedItem.Matrix));
				this.notifyPartChanged?.Invoke();
			}
		}

		public bool SuppressUiVolumes { get; set; } = false;

		public bool MouseDownOnInteractionVolume => SelectedInteractionVolume != null;

		public InteractionVolume SelectedInteractionVolume { get; set; } = null;
		public InteractionVolume HoveredInteractionVolume { get; set; } = null;

		public double SnapGridDistance { get; set; } = 1;

		public GuiWidget GuiSurface => this;

		private void OnDrawGlContent(DrawEventArgs e)
		{
			DrawGlOpaqueContent?.Invoke(this, e);
			DrawGlTransparentContent?.Invoke(this, e);
		}

		public static void SetGlContext(WorldView worldView, RectangleDouble screenRect, LightingData lighting)
		{
			GL.ClearDepth(1.0);
			GL.Clear(ClearBufferMask.DepthBufferBit);   // Clear the Depth Buffer

			GL.PushAttrib(AttribMask.ViewportBit);
			GL.Viewport((int)screenRect.Left, (int)screenRect.Bottom, (int)screenRect.Width, (int)screenRect.Height);

			GL.ShadeModel(ShadingModel.Smooth);

			GL.FrontFace(FrontFaceDirection.Ccw);
			GL.CullFace(CullFaceMode.Back);

			GL.DepthFunc(DepthFunction.Lequal);

			GL.Disable(EnableCap.DepthTest);
			//ClearToGradient();

			GL.Light(LightName.Light0, LightParameter.Ambient, lighting.ambientLight);

			GL.Light(LightName.Light0, LightParameter.Diffuse, lighting.diffuseLight0);
			GL.Light(LightName.Light0, LightParameter.Specular, lighting.specularLight0);

			GL.Light(LightName.Light0, LightParameter.Ambient, new float[] { 0, 0, 0, 0 });
			GL.Light(LightName.Light1, LightParameter.Diffuse, lighting.diffuseLight1);
			GL.Light(LightName.Light1, LightParameter.Specular, lighting.specularLight1);

			GL.ColorMaterial(MaterialFace.FrontAndBack, ColorMaterialParameter.AmbientAndDiffuse);

			GL.Enable(EnableCap.Light0);
			GL.Enable(EnableCap.Light1);
			GL.Enable(EnableCap.DepthTest);
			GL.Enable(EnableCap.Blend);
			GL.Enable(EnableCap.Normalize);
			GL.Enable(EnableCap.Lighting);
			GL.Enable(EnableCap.ColorMaterial);

			Vector3 lightDirectionVector = new Vector3(lighting.lightDirection0[0], lighting.lightDirection0[1], lighting.lightDirection0[2]);
			lightDirectionVector.Normalize();
			lighting.lightDirection0[0] = (float)lightDirectionVector.X;
			lighting.lightDirection0[1] = (float)lightDirectionVector.Y;
			lighting.lightDirection0[2] = (float)lightDirectionVector.Z;
			GL.Light(LightName.Light0, LightParameter.Position, lighting.lightDirection0);
			GL.Light(LightName.Light1, LightParameter.Position, lighting.lightDirection1);

			// set the projection matrix
			GL.MatrixMode(MatrixMode.Projection);
			GL.PushMatrix();
			GL.LoadMatrix(worldView.ProjectionMatrix.GetAsDoubleArray());

			// set the modelview matrix
			GL.MatrixMode(MatrixMode.Modelview);
			GL.PushMatrix();
			GL.LoadMatrix(worldView.ModelviewMatrix.GetAsDoubleArray());
		}

		public static void UnsetGlContext()
		{
			GL.MatrixMode(MatrixMode.Projection);
			GL.PopMatrix();

			GL.MatrixMode(MatrixMode.Modelview);
			GL.PopMatrix();

			GL.Disable(EnableCap.ColorMaterial);
			GL.Disable(EnableCap.Lighting);
			GL.Disable(EnableCap.Light0);
			GL.Disable(EnableCap.Light1);

			GL.Disable(EnableCap.Normalize);
			GL.Disable(EnableCap.Blend);
			GL.Disable(EnableCap.DepthTest);

			GL.PopAttrib();
		}
	}
}