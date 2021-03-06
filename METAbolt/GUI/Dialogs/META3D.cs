﻿//  This code is largely based on the PrimWorkshop form of Radegast
//  Parts are based on the PrimWorkshop project of libopenmv
//
//  Copyright (c) 2009-2011, Radegast Development Team
//  Copyright (c) 2008 - 2014, www.metabolt.net (METAbolt)
//  All rights reserved.

//  Redistribution and use in source and binary forms, with or without modification, 
//  are permitted provided that the following conditions are met:

//  * Redistributions of source code must retain the above copyright notice, 
//    this list of conditions and the following disclaimer. 
//  * Redistributions in binary form must reproduce the above copyright notice, 
//    this list of conditions and the following disclaimer in the documentation 
//    and/or other materials provided with the distribution. 
//  * Neither the name METAbolt nor the names of its contributors may be used to 
//    endorse or promote products derived from this software without specific prior 
//    written permission.

//  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND 
//  ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED 
//  WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
//  IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, 
//  INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT 
//  NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR 
//  PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, 
//  WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
//  ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
//  POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenMetaverse.Imaging;
using OpenMetaverse.Rendering;
using OpenMetaverse.Assets;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Platform;
using Utilities = OpenTK.Platform.Utilities;
//using SLNetworkComm;
using System.Threading;
using System.Diagnostics;
using PopupControl;
using System.Media;
using ExceptionReporting;
using System.Globalization;


namespace METAbolt
{
    public partial class META3D : Form
    {
        public OpenTK.GLControl glControl = null;
        public bool UseMultiSampling = false;   //true;
        public bool RenderingEnabled = false;
        Dictionary<uint, FacetedMesh> Prims = new Dictionary<uint, FacetedMesh>();
        public uint RootPrimLocalID = 0;
        public OpenMetaverse.Vector3 Center = new OpenMetaverse.Vector3(OpenMetaverse.Vector3.Zero);

        private TextRendering textRendering;
        private OpenTK.Matrix4 ModelMatrix;
        private OpenTK.Matrix4 ProjectionMatrix;
        private int[] Viewport = new int[4];

        public enum RenderPass
        {
            Picking,
            Simple,
            Alpha
        }

        //private OpenTK.Graphics.IGraphicsContextInternal context;
        private bool MipmapsSupported = false;

#pragma warning disable 0612
        public TextPrinter Printer = new TextPrinter(OpenTK.Graphics.TextQuality.High);
#pragma warning restore 0612

        private Popup toolTip;
        private CustomToolTip customToolTip;

        int[] TexturePointers = new int[1];
        Dictionary<UUID, Image> Textures = new Dictionary<UUID, Image>();
        METAboltInstance instance;
        private GridClient client;
        //private SLNetCom netcom;
        MeshmerizerR renderer;
        OpenTK.Graphics.GraphicsMode GLMode = null;
        AutoResetEvent TextureThreadContextReady = new AutoResetEvent(false);
        BlockingQueue<TextureLoadItem> PendingTextures = new BlockingQueue<TextureLoadItem>();
        float[] lightPos = new float[] { 0f, 0f, 1f, 0f };

        private bool TakeScreenShot = false;
        private bool snapped = false;
        bool dragging = false;
        int dragX, dragY, downX, downY;
        bool TextureThreadRunning = true;
        private Color clearcolour = Color.RoyalBlue;
        public ObjectsListItem objectitem;
        public bool isobject = true;
        public bool enablemipmapd = true;

        //static readonly Font TextFont = new Font(FontFamily.GenericSansSerif, 10);
        //Bitmap TextBitmap;   // = new Bitmap(512, 512);
        //int texture;
        //bool viewport_changed = true;
        private Primitive selitem = new Primitive();
        private bool msgdisplayed = false;


        private ExceptionReporter reporter = new ExceptionReporter();

        internal class ThreadExceptionHandler
        {
            public void ApplicationThreadException(object sender, ThreadExceptionEventArgs e)
            {
                ExceptionReporter reporter = new ExceptionReporter();
                reporter.Show(e.Exception);
            }
        }

        void META3D_Disposed(object sender, EventArgs e)
        {
            try
            {
                if (glControl != null)
                {
                    glControl.Dispose();
                    glControl = null;
                }                

                if (textRendering != null)
                {
                    textRendering = null;
                }

            }
            catch (Exception ex)
            {
                //string exp = ex.Message;
                reporter.Show(ex);
            }
        }

        public META3D(METAboltInstance instance, uint rootLocalID, Primitive item)
        {
            InitializeComponent();

            SetExceptionReporter();
            Application.ThreadException += new ThreadExceptionHandler().ApplicationThreadException;

            Disposed += new EventHandler(META3D_Disposed);

            this.RootPrimLocalID = rootLocalID;

            selitem = item;

            string msg1 = "Drag (left mouse down) to rotate object\n" +
                            "ALT+Drag to Zoom\n" +
                            "Ctrl+Drag to Pan\n" +
                            "Wheel in/out to Zoom in/out\n\n" +
                            "Click camera then object for snapshot";

            toolTip = new Popup(customToolTip = new CustomToolTip(instance, msg1));
            toolTip.AutoClose = false;
            toolTip.FocusOnOpen = false;
            toolTip.ShowingAnimation = toolTip.HidingAnimation = PopupAnimations.Blend;

            UseMultiSampling = false;

            this.instance = instance;
            client = this.instance.Client;
            //netcom = this.instance.Netcom;
            isobject = false;

            TexturePointers[0] = 0;

            renderer = new MeshmerizerR();
            textRendering = new TextRendering(instance);

            client.Objects.TerseObjectUpdate += new EventHandler<TerseObjectUpdateEventArgs>(Objects_TerseObjectUpdate);
            client.Objects.ObjectUpdate += new EventHandler<PrimEventArgs>(Objects_ObjectUpdate);
            client.Objects.ObjectDataBlockUpdate += new EventHandler<ObjectDataBlockUpdateEventArgs>(Objects_ObjectDataBlockUpdate);
            client.Network.SimChanged += new EventHandler<SimChangedEventArgs>(SIM_OnSimChanged);
            client.Self.TeleportProgress += new EventHandler<TeleportEventArgs>(Self_TeleportProgress);
        }

        public META3D(METAboltInstance instance, ObjectsListItem obtectitem)
        {
            InitializeComponent();

            SetExceptionReporter();
            Application.ThreadException += new ThreadExceptionHandler().ApplicationThreadException;

            Disposed += new EventHandler(META3D_Disposed);

            this.RootPrimLocalID = obtectitem.Prim.LocalID;

            selitem = obtectitem.Prim;

            string msg1 = "Drag (left mouse down) to rotate object\n" +
                            "ALT+Drag to Zoom\n" +
                            "Ctrl+Drag to Pan\n" +
                            "Wheel in/out to Zoom in/out\n\n" +
                            "Click camera then object for snapshot";

            toolTip = new Popup(customToolTip = new CustomToolTip(instance, msg1));
            toolTip.AutoClose = false;
            toolTip.FocusOnOpen = false;
            toolTip.ShowingAnimation = toolTip.HidingAnimation = PopupAnimations.Blend;

            UseMultiSampling = false;

            this.instance = instance;
            client = this.instance.Client;
            //netcom = this.instance.Netcom;
            isobject = true;
            this.objectitem = obtectitem;

            TexturePointers[0] = 0;

            renderer = new MeshmerizerR();
            textRendering = new TextRendering(instance);

            client.Objects.TerseObjectUpdate += new EventHandler<TerseObjectUpdateEventArgs>(Objects_TerseObjectUpdate);
            client.Objects.ObjectUpdate += new EventHandler<PrimEventArgs>(Objects_ObjectUpdate);
            client.Objects.ObjectDataBlockUpdate += new EventHandler<ObjectDataBlockUpdateEventArgs>(Objects_ObjectDataBlockUpdate);
            client.Network.SimChanged += new EventHandler<SimChangedEventArgs>(SIM_OnSimChanged);
            client.Self.TeleportProgress += new EventHandler<TeleportEventArgs>(Self_TeleportProgress);
        }

        private void SetExceptionReporter()
        {
            reporter.Config.ShowSysInfoTab = false;   // alternatively, set properties programmatically
            reporter.Config.ShowFlatButtons = true;   // this particular config is code-only
            reporter.Config.CompanyName = "METAbolt";
            reporter.Config.ContactEmail = "metabolt@vistalogic.co.uk";
            reporter.Config.EmailReportAddress = "metabolt@vistalogic.co.uk";
            reporter.Config.WebUrl = "http://www.metabolt.net/metaforums/";
            reporter.Config.AppName = "METAbolt";
            reporter.Config.MailMethod = ExceptionReporting.Core.ExceptionReportInfo.EmailMethod.SimpleMAPI;
            reporter.Config.BackgroundColor = Color.White;
            reporter.Config.ShowButtonIcons = false;
            reporter.Config.ShowLessMoreDetailButton = true;
            reporter.Config.TakeScreenshot = true;
            reporter.Config.ShowContactTab = true;
            reporter.Config.ShowExceptionsTab = true;
            reporter.Config.ShowFullDetail = true;
            reporter.Config.ShowGeneralTab = true;
            reporter.Config.ShowSysInfoTab = true;
            reporter.Config.TitleText = "METAbolt Exception Reporter";
        }

        //void META3D_Disposed(object sender, EventArgs e)
        //{
        //    try
        //    {
        //        if (glControl != null)
        //        {
        //            glControl.Dispose();
        //        }

        //        glControl = null;

        //        base.Dispose(true);

        //        client.Objects.TerseObjectUpdate -= new EventHandler<TerseObjectUpdateEventArgs>(Objects_TerseObjectUpdate);
        //        client.Objects.ObjectUpdate -= new EventHandler<PrimEventArgs>(Objects_ObjectUpdate);
        //        client.Objects.ObjectDataBlockUpdate -= new EventHandler<ObjectDataBlockUpdateEventArgs>(Objects_ObjectDataBlockUpdate);
        //        client.Network.SimChanged -= new EventHandler<SimChangedEventArgs>(SIM_OnSimChanged);
        //        client.Self.TeleportProgress -= new EventHandler<TeleportEventArgs>(Self_TeleportProgress);

        //        lock (this.Prims)
        //        {
        //            Prims.Clear();
        //        }

        //        lock (this.Textures)
        //        {
        //            Textures.Clear();
        //        }

        //        //GC.Collect();
        //        //GC.WaitForPendingFinalizers();
        //    }
        //    catch (Exception ex)
        //    {
        //        string exp = ex.Message;
        //        reporter.Show(ex);
        //    }
        //}

        private void SIM_OnSimChanged(object sender, SimChangedEventArgs e)
        {
            if (!this.IsHandleCreated) return;

            lock (Prims)
            {
                Prims.Clear();
            }

            lock (this.Textures)
            {
                Textures.Clear();
            }
        }

        private void Self_TeleportProgress(object sender, TeleportEventArgs e)
        {
            if (!this.IsHandleCreated) return;

            switch (e.Status)
            {
                case TeleportStatus.Start:
                case TeleportStatus.Progress:
                    this.RenderingEnabled = false;
                    return;

                case TeleportStatus.Failed:
                case TeleportStatus.Cancelled:
                    this.RenderingEnabled = true;
                    return;

                case TeleportStatus.Finished:
                    WorkPool.QueueUserWorkItem(delegate(object sync)
                    {
                        Cursor.Current = Cursors.WaitCursor;
                        Thread.Sleep(6000);
                        ReLoadObject();
                        RenderingEnabled = true;
                        Cursor.Current = Cursors.Default;
                    });

                    return;
            }
        }

        private void ReLoadObject()
        {
            WorkPool.QueueUserWorkItem(delegate(object sync)
            {
                // Search for the new local id of the object
                List<Primitive> results = client.Network.CurrentSim.ObjectsPrimitives.FindAll(
                    new Predicate<Primitive>(delegate(Primitive prim)
                    {
                        try
                        {
                            return (prim.ID == selitem.ID);
                        }
                        catch
                        {
                            return false;
                        }
                    }));

                if (results != null)
                {
                    try
                    {
                        selitem = results[0];

                        RootPrimLocalID = selitem.LocalID;

                        if (client.Network.CurrentSim.ObjectsPrimitives.ContainsKey(RootPrimLocalID))
                        {
                            UpdatePrimBlocking(client.Network.CurrentSim.ObjectsPrimitives[RootPrimLocalID]);
                            var children = client.Network.CurrentSim.ObjectsPrimitives.FindAll((Primitive p) => { return p.ParentID == RootPrimLocalID; });
                            children.ForEach(p => UpdatePrimBlocking(p));
                        }
                    }
                    catch { ; }
                }
                else
                {
                    //this.Close();
                    this.Dispose();
                }
            });
        }

        void Objects_TerseObjectUpdate(object sender, TerseObjectUpdateEventArgs e)
        {
            if (!this.IsHandleCreated) return;

            if (Prims.ContainsKey(e.Prim.LocalID))
            {
                UpdatePrimBlocking(e.Prim);
            }
        }

        void Objects_ObjectUpdate(object sender, PrimEventArgs e)
        {
            if (!this.IsHandleCreated) return;

            if (Prims.ContainsKey(e.Prim.LocalID) || Prims.ContainsKey(e.Prim.ParentID))
            {
                UpdatePrimBlocking(e.Prim);
            }
        }

        void Objects_ObjectDataBlockUpdate(object sender, ObjectDataBlockUpdateEventArgs e)
        {
            if (!this.IsHandleCreated) return;

            if (Prims.ContainsKey(e.Prim.LocalID))
            {
                UpdatePrimBlocking(e.Prim);
            }
        }

        public void SetupGLControl()
        {
            RenderingEnabled = false;

            if (glControl != null)
            {
                glControl.Dispose();
            }

            glControl = null;

            GLMode = null;

            try
            {
                GLMode = new OpenTK.Graphics.GraphicsMode(OpenTK.DisplayDevice.Default.BitsPerPixel, 24, 8, 4);
            }
            catch
            {
                GLMode = null;
            }

            try
            {
                if (GLMode == null)
                {
                    // Try default mode
                    glControl = new OpenTK.GLControl();
                }
                else
                {
                    glControl = new OpenTK.GLControl(GLMode);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex.Message, Helpers.LogLevel.Warning, client);
                glControl = null;
            }

            if (glControl == null)
            {
                Logger.Log("Failed to initialize OpenGL control, cannot continue", Helpers.LogLevel.Error, client);
                return;
            }

            Logger.Log("Initializing OpenGL mode: " + GLMode.ToString(), Helpers.LogLevel.Info);

            glControl.Paint += glControl_Paint;
            glControl.Resize += glControl_Resize;
            glControl.MouseDown += glControl_MouseDown;
            glControl.MouseUp += glControl_MouseUp;
            glControl.MouseMove += glControl_MouseMove;
            glControl.MouseWheel += glControl_MouseWheel;
            glControl.Load += new EventHandler(glControl_Load);
            glControl.Disposed += new EventHandler(glControl_Disposed);
            glControl.Click += new EventHandler(glControl_Click);
            glControl.BackColor = clearcolour;

            glControl.Dock = DockStyle.Fill;
            glControl.TabIndex = 0;

            Controls.Add(glControl);

            glControl.BringToFront();
            glControl.Focus();
        }

        void glControl_Disposed(object sender, EventArgs e)
        {
            TextureThreadRunning = false;
            PendingTextures.Close();
        }

        void glControl_Click(object sender, EventArgs e)
        {
            if (TakeScreenShot)
            {
                snapped = true;
            }
        }

        void glControl_Load(object sender, EventArgs e)
        {
            try
            {
                OpenTK.Graphics.OpenGL.GL.ShadeModel(OpenTK.Graphics.OpenGL.ShadingModel.Smooth);
                OpenTK.Graphics.OpenGL.GL.ClearColor(clearcolour);
                glControl.BackColor = clearcolour;

                OpenTK.Graphics.OpenGL.GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.Lighting);
                OpenTK.Graphics.OpenGL.GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.Light0);
                OpenTK.Graphics.OpenGL.GL.Light(OpenTK.Graphics.OpenGL.LightName.Light0, OpenTK.Graphics.OpenGL.LightParameter.Ambient, new float[] { 0.5f, 0.5f, 0.5f, 1f });
                OpenTK.Graphics.OpenGL.GL.Light(OpenTK.Graphics.OpenGL.LightName.Light0, OpenTK.Graphics.OpenGL.LightParameter.Diffuse, new float[] { 0.3f, 0.3f, 0.3f, 1f });
                OpenTK.Graphics.OpenGL.GL.Light(OpenTK.Graphics.OpenGL.LightName.Light0, OpenTK.Graphics.OpenGL.LightParameter.Specular, new float[] { 0.8f, 0.8f, 0.8f, 1.0f });
                OpenTK.Graphics.OpenGL.GL.Light(OpenTK.Graphics.OpenGL.LightName.Light0, OpenTK.Graphics.OpenGL.LightParameter.Position, lightPos);
                //OpenTK.Graphics.OpenGL.GL.Light(OpenTK.Graphics.OpenGL.LightName.Light0, OpenTK.Graphics.OpenGL.LightParameter.LinearAttenuation, lightPos);
                //OpenTK.Graphics.OpenGL.GL.Light(OpenTK.Graphics.OpenGL.LightName.Light0, OpenTK.Graphics.OpenGL.LightParameter.QuadraticAttenuation, lightPos);
                //OpenTK.Graphics.OpenGL.GL.Light(OpenTK.Graphics.OpenGL.LightName.Light0, OpenTK.Graphics.OpenGL.LightParameter.SpotDirection, lightPos);
                //OpenTK.Graphics.OpenGL.GL.Light(OpenTK.Graphics.OpenGL.LightName.Light0, OpenTK.Graphics.OpenGL.LightParameter.SpotExponent, lightPos);

                //OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.Lighting);
                //OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.Light0); 

                OpenTK.Graphics.OpenGL.GL.ClearDepth(1.0d);
                OpenTK.Graphics.OpenGL.GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.DepthTest);
                OpenTK.Graphics.OpenGL.GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.ColorMaterial);
                OpenTK.Graphics.OpenGL.GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.CullFace);
                OpenTK.Graphics.OpenGL.GL.ColorMaterial(OpenTK.Graphics.OpenGL.MaterialFace.Front, OpenTK.Graphics.OpenGL.ColorMaterialParameter.AmbientAndDiffuse);
                OpenTK.Graphics.OpenGL.GL.ColorMaterial(OpenTK.Graphics.OpenGL.MaterialFace.Front, OpenTK.Graphics.OpenGL.ColorMaterialParameter.Specular);

                OpenTK.Graphics.OpenGL.GL.DepthMask(true);
                OpenTK.Graphics.OpenGL.GL.DepthFunc(OpenTK.Graphics.OpenGL.DepthFunction.Lequal);
                OpenTK.Graphics.OpenGL.GL.Hint(OpenTK.Graphics.OpenGL.HintTarget.PerspectiveCorrectionHint, OpenTK.Graphics.OpenGL.HintMode.Nicest);
                OpenTK.Graphics.OpenGL.GL.MatrixMode(OpenTK.Graphics.OpenGL.MatrixMode.Projection);

                OpenTK.Graphics.OpenGL.GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.Blend);
                OpenTK.Graphics.OpenGL.GL.BlendFunc(OpenTK.Graphics.OpenGL.BlendingFactorSrc.SrcAlpha, OpenTK.Graphics.OpenGL.BlendingFactorDest.OneMinusSrcAlpha);

                //enablemipmapd = OpenTK.Graphics.OpenGL.GL.GetString(OpenTK.Graphics.OpenGL.StringName.Extensions).Contains("GL_SGIS_generate_mipmap");

                try
                {
                    OpenTK.Graphics.IGraphicsContextInternal context = glControl.Context as OpenTK.Graphics.IGraphicsContextInternal;
                    MipmapsSupported = context.GetAddress("glGenerateMipmap") != IntPtr.Zero;
                }
                catch
                {
                    //string exp = ex.Message;
                    MipmapsSupported = false;
                }

                RenderingEnabled = true;

                // Call the resizing function which sets up the GL drawing window
                // and will also invalidate the GL control
                glControl_Resize(null, null);

                glControl.Context.MakeCurrent(null);
                TextureThreadContextReady.Reset();

                var textureThread = new Thread(() => TextureThread())
                {
                    IsBackground = true,
                    Name = "TextureLoadingThread"
                };

                textureThread.Start();
                TextureThreadContextReady.WaitOne(1000, false);
                glControl.MakeCurrent();

                GLInvalidate();

                Logger.Log("OpenGL control initialised", Helpers.LogLevel.Info , client);
                //Application.Idle += Application_Idle;
                //sw.Start(); 
            }
            catch (Exception ex)
            {
                RenderingEnabled = false;
                Logger.Log("Failed to initialize OpenGL control", Helpers.LogLevel.Warning, client, ex);
            }
        }

        //float rotation = 0;
        //void Application_Idle(object sender, EventArgs e)
        //{
        //    double milliseconds = ComputeTimeSlice();
        //    Accumulate(milliseconds);
        //    Animate(milliseconds);
        //}

        //private double ComputeTimeSlice()
        //{
        //    sw.Stop();
        //    double timeslice = sw.Elapsed.TotalMilliseconds;
        //    sw.Reset();
        //    sw.Start();
        //    return timeslice;
        //}

        //private void Animate(double milliseconds)
        //{
        //    float deltaRotation = (float)milliseconds / 20.0f;
        //    rotation += deltaRotation;
        //    glControl.Invalidate();

        //    WorkPool.QueueUserWorkItem(sync =>
        //    {
        //        if (client.Network.CurrentSim.ObjectsPrimitives.ContainsKey(RootPrimLocalID))
        //        {
        //            UpdatePrimBlocking(client.Network.CurrentSim.ObjectsPrimitives[RootPrimLocalID]);
        //            var children = client.Network.CurrentSim.ObjectsPrimitives.FindAll((Primitive p) => { return p.ParentID == RootPrimLocalID; });
        //            children.ForEach(p => UpdatePrimBlocking(p));
        //        }
        //    }
        //    );
        //}

        //double accumulator = 0;
        //int idleCounter = 0;
        //private void Accumulate(double milliseconds)
        //{
        //    idleCounter++;
        //    accumulator += milliseconds;
        //    if (accumulator > 1000)
        //    {
        //        label1.Text = idleCounter.ToString();
        //        accumulator -= 1000;
        //        idleCounter = 0; // don't forget to reset the counter!
        //    }
        //}

        private void glControl_Paint(object sender, PaintEventArgs e)
        {
            if (!RenderingEnabled) return;

            //// A LL
            OpenTK.Graphics.OpenGL.GL.Clear(OpenTK.Graphics.OpenGL.ClearBufferMask.ColorBufferBit | OpenTK.Graphics.OpenGL.ClearBufferMask.DepthBufferBit);

            glControl.MakeCurrent();

            Render(false);

            OpenTK.Graphics.OpenGL.GL.ClearColor(clearcolour);

            glControl.SwapBuffers();

            // A LL
            if (TakeScreenShot)
            {
                if (snapped)
                {
                    SoundPlayer simpleSound = new SoundPlayer(Properties.Resources.camera_clic_with_flash);
                    simpleSound.Play();
                    simpleSound.Dispose();

                    capScreenBeforeNextSwap();
                    TakeScreenShot = false;
                    snapped = false;
                }
            }
        }

        private void glControl_Resize(object sender, EventArgs e)
        {
            if (!RenderingEnabled) return;

            glControl.MakeCurrent();

            //viewport_changed = true;

            //TextBitmap.Dispose();
            //TextBitmap = new Bitmap(ClientSize.Width, ClientSize.Height);

            //OpenTK.Graphics.OpenGL.GL.BindTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D,texture);
            //OpenTK.Graphics.OpenGL.GL.TexSubImage2D(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 0, 0, 0, TextBitmap.Width, TextBitmap.Height,
            //    OpenTK.Graphics.OpenGL.PixelFormat.Bgra, OpenTK.Graphics.OpenGL.PixelType.UnsignedByte, IntPtr.Zero);

            OpenTK.Graphics.OpenGL.GL.ClearColor(clearcolour);   //   0.39f, 0.58f, 0.93f, 1.0f);

            if (glControl.ClientSize.Height == 0)
                glControl.ClientSize = new System.Drawing.Size(glControl.ClientSize.Width, 1);

            OpenTK.Graphics.OpenGL.GL.Viewport(0, 0, glControl.ClientSize.Width, glControl.ClientSize.Height);

            OpenTK.Graphics.OpenGL.GL.PushMatrix();
            OpenTK.Graphics.OpenGL.GL.MatrixMode(OpenTK.Graphics.OpenGL.MatrixMode.Projection);
            OpenTK.Graphics.OpenGL.GL.LoadIdentity();


            float dAspRat = (float)glControl.Width / (float)glControl.Height;
            GluPerspective(50f, dAspRat, 0.1f, 100.0f);

            OpenTK.Graphics.OpenGL.GL.MatrixMode(OpenTK.Graphics.OpenGL.MatrixMode.Modelview);
            OpenTK.Graphics.OpenGL.GL.PopMatrix();

            //// A LL
            GLInvalidate();
        }

        #region Mouse handling


        private void glControl_MouseWheel(object sender, MouseEventArgs e)
        {
            int newVal = Utils.Clamp(scrollZoom.Value + e.Delta / 95, scrollZoom.Minimum, scrollZoom.Maximum);

            if (scrollZoom.Value != newVal)
            {
                scrollZoom.Value = newVal;
                glControl_Resize(null, null);
                GLInvalidate();
            }
        }

        FacetedMesh RightclickedPrim;
        int RightclickedFaceID;

        private void glControl_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
            {
                dragging = true;
                downX = dragX = e.X;
                downY = dragY = e.Y;
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (TryPick(e.X, e.Y, out RightclickedPrim, out RightclickedFaceID))
                {
                    ctxObjects.Show(glControl, e.X, e.Y);
                }
            }
        }

        private void glControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                int deltaX = e.X - dragX;
                int deltaY = e.Y - dragY;

                if (e.Button == MouseButtons.Left)
                {
                    if (ModifierKeys == Keys.Control || ModifierKeys == (Keys.Alt | Keys.Control | Keys.Shift))
                    {
                        Center.X -= deltaX / 100f;
                        Center.Z += deltaY / 100f;
                    }

                    if (ModifierKeys == Keys.Alt)
                    {
                        Center.Y -= deltaY / 25f;

                        int newYaw = scrollYaw.Value + deltaX;
                        if (newYaw < 0) newYaw += 360;
                        if (newYaw > 360) newYaw -= 360;

                        scrollYaw.Value = newYaw;
                    }

                    if (ModifierKeys == Keys.None || ModifierKeys == (Keys.Alt | Keys.Control))
                    {
                        int newRoll = scrollRoll.Value + deltaY;
                        if (newRoll < 0) newRoll += 360;
                        if (newRoll > 360) newRoll -= 360;

                        scrollRoll.Value = newRoll;


                        int newYaw = scrollYaw.Value + deltaX;
                        if (newYaw < 0) newYaw += 360;
                        if (newYaw > 360) newYaw -= 360;

                        scrollYaw.Value = newYaw;
                    }
                }
                else if (e.Button == MouseButtons.Middle)
                {
                    Center.X -= deltaX / 100f;
                    Center.Z += deltaY / 100f;

                }

                dragX = e.X;
                dragY = e.Y;

                GLInvalidate();
            }
        }

        private void glControl_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = false;

                if (e.X == downX && e.Y == downY) // click
                {
                    FacetedMesh picked;
                    int faceID;

                    if (TryPick(e.X, e.Y, out picked, out faceID))
                    {
                        client.Self.Grab(picked.Prim.LocalID, OpenMetaverse.Vector3.Zero, OpenMetaverse.Vector3.Zero, OpenMetaverse.Vector3.Zero, faceID, OpenMetaverse.Vector3.Zero, OpenMetaverse.Vector3.Zero, OpenMetaverse.Vector3.Zero);
                        client.Self.DeGrab(picked.Prim.LocalID);
                    }
                }

                GLInvalidate();
            }
        }
        #endregion Mouse handling

        #region Texture thread
        void TextureThread()
        {
            try
            {
                OpenTK.INativeWindow newwindow = new OpenTK.NativeWindow();
                OpenTK.INativeWindow window = newwindow;

                OpenTK.Graphics.IGraphicsContext newcontext = new OpenTK.Graphics.GraphicsContext(GLMode, window.WindowInfo);
                OpenTK.Graphics.IGraphicsContext context = newcontext;
                context.MakeCurrent(window.WindowInfo);

                TextureThreadContextReady.Set();
                PendingTextures.Open();

                Logger.DebugLog("Started Texture Thread");

                while (window.Exists && TextureThreadRunning)
                {
                    window.ProcessEvents();

                    TextureLoadItem item = null;

                    if (!PendingTextures.Dequeue(Timeout.Infinite, ref item)) continue;

                    if (LoadTexture(item.TeFace.TextureID, ref item.Data.Texture, false))
                    {
                        OpenTK.Graphics.OpenGL.GL.GenTextures(1, out item.Data.TexturePointer);
                        OpenTK.Graphics.OpenGL.GL.BindTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, item.Data.TexturePointer);

                        Bitmap bitmap = new Bitmap(item.Data.Texture);

                        bool hasAlpha;

                        if (item.Data.Texture.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb)
                        {
                            hasAlpha = true;
                        }
                        else
                        {
                            hasAlpha = false;
                        }

                        item.Data.IsAlpha = hasAlpha;

                        bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);
                        Rectangle rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);


                        BitmapData bitmapData =
                            bitmap.LockBits(
                            rectangle,
                            ImageLockMode.ReadOnly,
                            hasAlpha ? System.Drawing.Imaging.PixelFormat.Format32bppArgb : System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                        OpenTK.Graphics.OpenGL.GL.TexImage2D(
                            OpenTK.Graphics.OpenGL.TextureTarget.Texture2D,
                            0,
                            hasAlpha ? OpenTK.Graphics.OpenGL.PixelInternalFormat.Rgba : OpenTK.Graphics.OpenGL.PixelInternalFormat.Rgb8,
                            bitmap.Width,
                            bitmap.Height,
                            0,
                            hasAlpha ? OpenTK.Graphics.OpenGL.PixelFormat.Bgra : OpenTK.Graphics.OpenGL.PixelFormat.Bgr,
                            OpenTK.Graphics.OpenGL.PixelType.UnsignedByte,
                            bitmapData.Scan0);

                        //// Auto detect if mipmaps are supported
                        //int[] MipMapCount = new int[1];
                        //OpenTK.Graphics.OpenGL.GL.GetTexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL.GetTextureParameter.TextureMaxLevel, out MipMapCount[0]);

                        //OpenTK.Graphics.OpenGL.GL.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL.TextureParameterName.TextureMagFilter, (int)OpenTK.Graphics.OpenGL.TextureMagFilter.Linear);

                        //if (MipMapCount[0] == 0) // if no MipMaps are present, use linear Filter
                        //{
                        //    OpenTK.Graphics.OpenGL.GL.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL.TextureParameterName.TextureMinFilter, (int)OpenTK.Graphics.OpenGL.TextureMinFilter.Linear);
                        //}
                        //else
                        //{
                        //    OpenTK.Graphics.OpenGL.GL.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL.TextureParameterName.TextureMinFilter, (int)OpenTK.Graphics.OpenGL.TextureMinFilter.LinearMipmapLinear);
                        //    OpenTK.Graphics.OpenGL.GL.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL.TextureParameterName.TextureWrapS, (int)OpenTK.Graphics.OpenGL.TextureWrapMode.Repeat);
                        //    OpenTK.Graphics.OpenGL.GL.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL.TextureParameterName.TextureWrapT, (int)OpenTK.Graphics.OpenGL.TextureWrapMode.Repeat);
                        //    OpenTK.Graphics.OpenGL.GL.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL.TextureParameterName.GenerateMipmap, 1);
                        //    OpenTK.Graphics.OpenGL.GL.GenerateMipmap(OpenTK.Graphics.OpenGL.GenerateMipmapTarget.Texture2D);
                        //}

                        //if (!enablemipmapd)

                        if (instance.Config.CurrentConfig.DisableMipmaps)
                        {
                            OpenTK.Graphics.OpenGL.GL.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL.TextureParameterName.TextureMinFilter, (int)OpenTK.Graphics.OpenGL.TextureMinFilter.Linear);
                        }
                        else
                        {
                            if (MipmapsSupported)
                            {
                                try
                                {
                                    OpenTK.Graphics.OpenGL.GL.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL.TextureParameterName.TextureMinFilter, (int)OpenTK.Graphics.OpenGL.TextureMinFilter.LinearMipmapLinear);
                                    OpenTK.Graphics.OpenGL.GL.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL.TextureParameterName.TextureWrapS, (int)OpenTK.Graphics.OpenGL.TextureWrapMode.Repeat);
                                    OpenTK.Graphics.OpenGL.GL.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL.TextureParameterName.TextureWrapT, (int)OpenTK.Graphics.OpenGL.TextureWrapMode.Repeat);
                                    OpenTK.Graphics.OpenGL.GL.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL.TextureParameterName.GenerateMipmap, 1);
                                    OpenTK.Graphics.OpenGL.GL.GenerateMipmap(OpenTK.Graphics.OpenGL.GenerateMipmapTarget.Texture2D);
                                }
                                catch
                                {
                                    OpenTK.Graphics.OpenGL.GL.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL.TextureParameterName.TextureMinFilter, (int)OpenTK.Graphics.OpenGL.TextureMinFilter.Linear);

                                    if (!msgdisplayed)
                                    {
                                        //Logger.Log("META3D TextureThread: Your video card does not support Mipmap. Try disabling Mipmaps from META3D tab under the Application/Preferences menu", Helpers.LogLevel.Warning);
                                        MessageBox.Show("Your video card does not support Mipmaps.\nDisable Mipmaps from the META3D tab under\nthe Application/Preferences menu");
                                        msgdisplayed = true;
                                    }
                                }
                            }
                            else
                            {
                                OpenTK.Graphics.OpenGL.GL.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL.TextureParameterName.TextureMinFilter, (int)OpenTK.Graphics.OpenGL.TextureMinFilter.Linear);

                                if (!msgdisplayed)
                                {
                                    MessageBox.Show("Your video card does not support Mipmaps.\nDisable Mipmaps from the META3D tab under\nthe Application/Preferences menu");
                                    msgdisplayed = true;
                                }
                            }
                        }

                        bitmap.UnlockBits(bitmapData);
                        bitmap.Dispose();

                        OpenTK.Graphics.OpenGL.GL.Flush();

                        GLInvalidate();
                    }
                }

                newcontext.Dispose();
                newwindow.Dispose();

                //Logger.DebugLog("Texture thread exited");
            }
            catch (Exception ex)
            {
                Logger.Log("META3D TextureThread: " + ex.Message, Helpers.LogLevel.Warning);
            }
        }
        #endregion Texture thread

        private void META3D_Shown(object sender, EventArgs e)
        {
            SetupGLControl();

            WorkPool.QueueUserWorkItem(sync =>
            {
                if (client.Network.CurrentSim.ObjectsPrimitives.ContainsKey(RootPrimLocalID))
                {
                    UpdatePrimBlocking(client.Network.CurrentSim.ObjectsPrimitives[RootPrimLocalID]);
                    var children = client.Network.CurrentSim.ObjectsPrimitives.FindAll((Primitive p) => { return p.ParentID == RootPrimLocalID; });
                    children.ForEach(p => UpdatePrimBlocking(p));
                }
            }
            );
        }

        #region Public methods
        //public void SetView(OpenMetaverse.Vector3 center, int roll, int pitch, int yaw, int zoom)
        //{
        //    this.Center = center;
        //    scrollRoll.Value = roll;
        //    scrollPitch.Value = pitch;
        //    scrollYaw.Value = yaw;
        //    scrollZoom.Value = zoom;
        //}

        public static FacetedMesh GenerateFacetedMesh(Primitive prim, OSDMap MeshData, DetailLevel LOD)
        {
            FacetedMesh ret = new FacetedMesh();

            ret.Faces = new List<Face>();
            ret.Prim = prim;
            ret.Profile = new Profile();
            ret.Profile.Faces = new List<ProfileFace>();
            ret.Profile.Positions = new List<OpenMetaverse.Vector3>();
            ret.Path = new OpenMetaverse.Rendering.Path();
            ret.Path.Points = new List<PathPoint>();

            try
            {
                OSD facesOSD = null;

                switch (LOD)
                {
                    default:
                    case DetailLevel.Highest:
                        facesOSD = MeshData["high_lod"];
                        break;

                    case DetailLevel.High:
                        facesOSD = MeshData["medium_lod"];
                        break;

                    case DetailLevel.Medium:
                        facesOSD = MeshData["low_lod"];
                        break;

                    case DetailLevel.Low:
                        facesOSD = MeshData["lowest_lod"];
                        break;
                }

                if (facesOSD == null || !(facesOSD is OSDArray))
                {
                    return ret;
                }

                OSDArray decodedMeshOsdArray = (OSDArray)facesOSD;

                for (int faceNr = 0; faceNr < decodedMeshOsdArray.Count; faceNr++)
                {
                    OSD subMeshOsd = decodedMeshOsdArray[faceNr];
                    Face oface = new Face();
                    oface.ID = faceNr;
                    oface.Vertices = new List<Vertex>();
                    oface.Indices = new List<ushort>();
                    oface.TextureFace = prim.Textures.GetFace((uint)faceNr);

                    if (subMeshOsd is OSDMap)
                    {
                        OSDMap subMeshMap = (OSDMap)subMeshOsd;

                        OpenMetaverse.Vector3 posMax = new OpenMetaverse.Vector3();
                        posMax = ((OSDMap)subMeshMap["PositionDomain"])["Max"];
                        OpenMetaverse.Vector3 posMin = new OpenMetaverse.Vector3();
                        posMin = ((OSDMap)subMeshMap["PositionDomain"])["Min"];

                        OpenMetaverse.Vector2 texPosMax = new OpenMetaverse.Vector2();
                        texPosMax = ((OSDMap)subMeshMap["TexCoord0Domain"])["Max"];
                        OpenMetaverse.Vector2 texPosMin = new OpenMetaverse.Vector2();
                        texPosMin = ((OSDMap)subMeshMap["TexCoord0Domain"])["Min"];


                        byte[] posBytes = subMeshMap["Position"];
                        byte[] norBytes = subMeshMap["Normal"];
                        byte[] texBytes = subMeshMap["TexCoord0"];

                        for (int i = 0; i < posBytes.Length; i += 6)
                        {
                            ushort uX = Utils.BytesToUInt16(posBytes, i);
                            ushort uY = Utils.BytesToUInt16(posBytes, i + 2);
                            ushort uZ = Utils.BytesToUInt16(posBytes, i + 4);

                            Vertex vx = new Vertex();

                            vx.Position = new OpenMetaverse.Vector3(
                                Utils.UInt16ToFloat(uX, posMin.X, posMax.X),
                                Utils.UInt16ToFloat(uY, posMin.Y, posMax.Y),
                                Utils.UInt16ToFloat(uZ, posMin.Z, posMax.Z));

                            ushort nX = Utils.BytesToUInt16(norBytes, i);
                            ushort nY = Utils.BytesToUInt16(norBytes, i + 2);
                            ushort nZ = Utils.BytesToUInt16(norBytes, i + 4);

                            vx.Normal = new OpenMetaverse.Vector3(
                                Utils.UInt16ToFloat(nX, posMin.X, posMax.X),
                                Utils.UInt16ToFloat(nY, posMin.Y, posMax.Y),
                                Utils.UInt16ToFloat(nZ, posMin.Z, posMax.Z));

                            var vertexIndexOffset = oface.Vertices.Count * 4;

                            if (texBytes != null && texBytes.Length >= vertexIndexOffset + 4)
                            {
                                ushort tX = Utils.BytesToUInt16(texBytes, vertexIndexOffset);
                                ushort tY = Utils.BytesToUInt16(texBytes, vertexIndexOffset + 2);

                                vx.TexCoord = new OpenMetaverse.Vector2(
                                    Utils.UInt16ToFloat(tX, texPosMin.X, texPosMax.X),
                                    Utils.UInt16ToFloat(tY, texPosMin.Y, texPosMax.Y));
                            }

                            oface.Vertices.Add(vx);
                        }

                        byte[] triangleBytes = subMeshMap["TriangleList"];
                        for (int i = 0; i < triangleBytes.Length; i += 6)
                        {
                            ushort v1 = (ushort)(Utils.BytesToUInt16(triangleBytes, i));
                            oface.Indices.Add(v1);
                            ushort v2 = (ushort)(Utils.BytesToUInt16(triangleBytes, i + 2));
                            oface.Indices.Add(v2);
                            ushort v3 = (ushort)(Utils.BytesToUInt16(triangleBytes, i + 4));
                            oface.Indices.Add(v3);
                        }
                    }

                    ret.Faces.Add(oface);
                }

            }
            catch (Exception ex)
            {
                Logger.Log("Failed to decode mesh asset: " + ex.Message, Helpers.LogLevel.Warning);
            }

            return ret;
        }
        #endregion Public methods

        #region Private methods (the meat)
        private OpenTK.Vector3 WorldToScreen(OpenTK.Vector3 world)
        {
            OpenTK.Vector3 screen = new OpenTK.Vector3();
            screen = OpenTK.Vector3.Zero;

            double[] ModelViewMatrix = new double[16];
            double[] ProjectionMatrix = new double[16];
            int[] Vport = new int[4];

            OpenTK.Graphics.OpenGL.GL.GetInteger(OpenTK.Graphics.OpenGL.GetPName.Viewport, Vport);
            OpenTK.Graphics.OpenGL.GL.GetDouble(OpenTK.Graphics.OpenGL.GetPName.ModelviewMatrix, ModelViewMatrix);
            OpenTK.Graphics.OpenGL.GL.GetDouble(OpenTK.Graphics.OpenGL.GetPName.ProjectionMatrix, ProjectionMatrix);

#pragma warning disable 0618
            OpenTK.Graphics.Glu.Project(world,
                ModelViewMatrix,
                ProjectionMatrix,
                Vport,
                out screen);
#pragma warning restore 0618

            screen.Y = glControl.Height - screen.Y;

            return screen;
        }

        private void RenderText()
        {
            lock (Prims)
            {
                int primNr = 0;

                foreach (FacetedMesh mesh in Prims.Values)
                {
                    primNr++;
                    Primitive prim = new Primitive();
                    prim = mesh.Prim;

                    if (!string.IsNullOrEmpty(prim.Text))
                    {
                        string text = System.Text.RegularExpressions.Regex.Replace(prim.Text, "(\r?\n)+", "\n");
                        OpenTK.Vector3 screenPos = new OpenTK.Vector3();
                        screenPos = OpenTK.Vector3.Zero;
                        OpenTK.Vector3 primPos = new OpenTK.Vector3();
                        primPos = OpenTK.Vector3.Zero;

                        if (!MipmapsSupported)
                        {
                            // Is it a child prim
                            if (prim.ParentID == RootPrimLocalID)
                            {
                                primPos = new OpenTK.Vector3(prim.Position.X, prim.Position.Y, prim.Position.Z);
                            }

                            OpenTK.Graphics.OpenGL.GL.MatrixMode(OpenTK.Graphics.OpenGL.MatrixMode.Modelview);

                            primPos.Z += prim.Scale.Z * 0.7f;
                            screenPos = WorldToScreen(primPos);

                            OpenTK.Graphics.OpenGL.GL.TexEnv(OpenTK.Graphics.OpenGL.TextureEnvTarget.TextureEnv, OpenTK.Graphics.OpenGL.TextureEnvParameter.TextureEnvMode, (float)OpenTK.Graphics.OpenGL.TextureEnvMode.ReplaceExt);
                            Color color = Color.FromArgb((int)(prim.TextColor.A * 255), (int)(prim.TextColor.R * 255), (int)(prim.TextColor.G * 255), (int)(prim.TextColor.B * 255));

                            Printer.Begin();

                            using (Font f = new Font(FontFamily.GenericSansSerif, 10f, FontStyle.Regular))
                            {
                                var size = Printer.Measure(text, f);
                                screenPos.X -= size.BoundingBox.Width / 2;
                                screenPos.Y -= size.BoundingBox.Height;

                                //Shadow
                                if (color != Color.DarkGray)
                                {
                                    Printer.Print(text, f, Color.DarkGray, new RectangleF(screenPos.X + 0.75f, screenPos.Y + 0.75f, size.BoundingBox.Width, size.BoundingBox.Height), OpenTK.Graphics.TextPrinterOptions.Default, OpenTK.Graphics.TextAlignment.Center);
                                }

                                Printer.Print(text, f, color, new RectangleF(screenPos.X, screenPos.Y, size.BoundingBox.Width, size.BoundingBox.Height), OpenTK.Graphics.TextPrinterOptions.Default, OpenTK.Graphics.TextAlignment.Center);
                            }

                            Printer.End();
                        }
                        else
                        {
                            // Is it child prim
                            FacetedMesh parent = null;

                            if (Prims.TryGetValue(prim.ParentID, out parent))
                            {
                                var newPrimPos = prim.Position * OpenMetaverse.Matrix4.CreateFromQuaternion(parent.Prim.Rotation);
                                primPos = new OpenTK.Vector3(newPrimPos.X, newPrimPos.Y, newPrimPos.Z);
                            }

                            primPos.Z += prim.Scale.Z * 0.8f;
                            if (!Math3D.GluProject(primPos, ModelMatrix, ProjectionMatrix, Viewport, out screenPos)) continue;
                            screenPos.Y = glControl.Height - screenPos.Y;

                            textRendering.Begin();

                            Color color = Color.FromArgb((int)(prim.TextColor.A * 255), (int)(prim.TextColor.R * 255), (int)(prim.TextColor.G * 255), (int)(prim.TextColor.B * 255));
                            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top;

                            using (Font f = new Font(FontFamily.GenericSansSerif, 10f, FontStyle.Regular))
                            {
                                var size = TextRendering.Measure(text, f, flags);
                                screenPos.X -= size.Width / 2;
                                screenPos.Y -= size.Height;

                                //Shadow
                                if (color != Color.DarkGray)
                                {
                                    textRendering.Print(text, f, Color.DarkGray, new Rectangle((int)screenPos.X + 1, (int)screenPos.Y + 1, size.Width, size.Height), flags);
                                }

                                textRendering.Print(text, f, color, new Rectangle((int)screenPos.X, (int)screenPos.Y, size.Width, size.Height), flags);
                            }

                            textRendering.End();
                        }
                    }
                }
            }
        }

        private void RenderObjects(RenderPass pass)
        {
            lock (Prims)
            {
                int primNr = 0;
                foreach (FacetedMesh mesh in Prims.Values)
                {
                    primNr++;
                    Primitive prim = new Primitive();
                    prim = mesh.Prim;
                    // Individual prim matrix
                    OpenTK.Graphics.OpenGL.GL.PushMatrix();

                    if (prim.ParentID == RootPrimLocalID)
                    {
                        FacetedMesh parent = null;
                        if (Prims.TryGetValue(prim.ParentID, out parent))
                        {
                            // Apply prim translation and rotation relative to the root prim
                            OpenTK.Graphics.OpenGL.GL.MultMatrix(Math3D.CreateRotationMatrix(parent.Prim.Rotation));
                            //GL.MultMatrixf(Math3D.CreateTranslationMatrix(parent.Prim.Position));
                        }

                        // Prim roation relative to root
                        OpenTK.Graphics.OpenGL.GL.MultMatrix(Math3D.CreateTranslationMatrix(prim.Position));
                    }

                    // Prim roation
                    OpenTK.Graphics.OpenGL.GL.MultMatrix(Math3D.CreateRotationMatrix(prim.Rotation));

                    // Prim scaling
                    OpenTK.Graphics.OpenGL.GL.Scale(prim.Scale.X, prim.Scale.Y, prim.Scale.Z);

                    // Draw the prim faces
                    for (int j = 0; j < mesh.Faces.Count; j++)
                    {
                        Primitive.TextureEntryFace teFace = mesh.Prim.Textures.FaceTextures[j];
                        Face face = mesh.Faces[j];
                        FaceData data = (FaceData)face.UserData;

                        if (teFace == null)
                            teFace = mesh.Prim.Textures.DefaultTexture;

                        if (pass != RenderPass.Picking)
                        {
                            bool belongToAlphaPass = (teFace.RGBA.A < 0.99) || data.IsAlpha;

                            if (belongToAlphaPass && pass != RenderPass.Alpha) continue;
                            if (!belongToAlphaPass && pass == RenderPass.Alpha) continue;

                            // Don't render transparent faces
                            if (teFace.RGBA.A <= 0.01f) continue;

                            switch (teFace.Shiny)
                            {
                                case Shininess.High:
                                    OpenTK.Graphics.OpenGL.GL.Material(OpenTK.Graphics.OpenGL.MaterialFace.Front, OpenTK.Graphics.OpenGL.MaterialParameter.Shininess, 94f);
                                    break;

                                case Shininess.Medium:
                                    OpenTK.Graphics.OpenGL.GL.Material(OpenTK.Graphics.OpenGL.MaterialFace.Front, OpenTK.Graphics.OpenGL.MaterialParameter.Shininess, 64f);
                                    break;

                                case Shininess.Low:
                                    OpenTK.Graphics.OpenGL.GL.Material(OpenTK.Graphics.OpenGL.MaterialFace.Front, OpenTK.Graphics.OpenGL.MaterialParameter.Shininess, 24f);
                                    break;


                                case Shininess.None:
                                default:
                                    OpenTK.Graphics.OpenGL.GL.Material(OpenTK.Graphics.OpenGL.MaterialFace.Front, OpenTK.Graphics.OpenGL.MaterialParameter.Shininess, 0f);
                                    break;
                            }

                            var faceColor = new float[] { teFace.RGBA.R, teFace.RGBA.G, teFace.RGBA.B, teFace.RGBA.A };

                            OpenTK.Graphics.OpenGL.GL.Color4(faceColor);
                            OpenTK.Graphics.OpenGL.GL.Material(OpenTK.Graphics.OpenGL.MaterialFace.Front, OpenTK.Graphics.OpenGL.MaterialParameter.AmbientAndDiffuse, faceColor);
                            OpenTK.Graphics.OpenGL.GL.Material(OpenTK.Graphics.OpenGL.MaterialFace.Front, OpenTK.Graphics.OpenGL.MaterialParameter.Specular, faceColor);

                            if (data.TexturePointer != 0)
                            {
                                OpenTK.Graphics.OpenGL.GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.Texture2D);
                            }
                            else
                            {
                                OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.Texture2D);
                            }

                            // Bind the texture
                            OpenTK.Graphics.OpenGL.GL.BindTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, data.TexturePointer);
                        }
                        else
                        {
                            data.PickingID = primNr;
                            var primNrBytes = Utils.Int16ToBytes((short)primNr);
                            var faceColor = new byte[] { primNrBytes[0], primNrBytes[1], (byte)j, 255 };

                            OpenTK.Graphics.OpenGL.GL.Color4(faceColor);
                        }

                        OpenTK.Graphics.OpenGL.GL.TexCoordPointer(2, OpenTK.Graphics.OpenGL.TexCoordPointerType.Float, 0, data.TexCoords);
                        OpenTK.Graphics.OpenGL.GL.VertexPointer(3, OpenTK.Graphics.OpenGL.VertexPointerType.Float, 0, data.Vertices);
                        OpenTK.Graphics.OpenGL.GL.NormalPointer(OpenTK.Graphics.OpenGL.NormalPointerType.Float, 0, data.Normals);
                        OpenTK.Graphics.OpenGL.GL.DrawElements(OpenTK.Graphics.OpenGL.BeginMode.Triangles, data.Indices.Length, OpenTK.Graphics.OpenGL.DrawElementsType.UnsignedShort, data.Indices);

                    }

                    // Pop the prim matrix
                    OpenTK.Graphics.OpenGL.GL.PopMatrix();
                }
            }
        }

        private void Render(bool picking)
        {
            OpenTK.Graphics.OpenGL.GL.Clear(OpenTK.Graphics.OpenGL.ClearBufferMask.ColorBufferBit | OpenTK.Graphics.OpenGL.ClearBufferMask.DepthBufferBit);
            OpenTK.Graphics.OpenGL.GL.LoadIdentity();

            glControl.MakeCurrent();

            if (picking)
            {
                OpenTK.Graphics.OpenGL.GL.ClearColor(clearcolour);
            }
            else
            {
                OpenTK.Graphics.OpenGL.GL.ClearColor(0.39f, 0.58f, 0.93f, 1.0f);
            }

            OpenTK.Graphics.OpenGL.GL.PolygonMode(OpenTK.Graphics.OpenGL.MaterialFace.FrontAndBack, OpenTK.Graphics.OpenGL.PolygonMode.Fill);

            var mLookAt = OpenTK.Matrix4d.LookAt(
                    Center.X, (double)scrollZoom.Value * 0.1d + Center.Y, Center.Z,
                    Center.X, Center.Y, Center.Z,
                    0d, 0d, 1d);
            OpenTK.Graphics.OpenGL.GL.MultMatrix(ref mLookAt);

            // Push the world matrix
            OpenTK.Graphics.OpenGL.GL.PushMatrix();

            OpenTK.Graphics.OpenGL.GL.EnableClientState(OpenTK.Graphics.OpenGL.ArrayCap.VertexArray);
            OpenTK.Graphics.OpenGL.GL.EnableClientState(OpenTK.Graphics.OpenGL.ArrayCap.TextureCoordArray);
            OpenTK.Graphics.OpenGL.GL.EnableClientState(OpenTK.Graphics.OpenGL.ArrayCap.NormalArray);

            // World rotations
            OpenTK.Graphics.OpenGL.GL.Rotate((float)scrollRoll.Value, 1f, 0f, 0f);
            OpenTK.Graphics.OpenGL.GL.Rotate((float)scrollPitch.Value, 0f, 1f, 0f);
            OpenTK.Graphics.OpenGL.GL.Rotate((float)scrollYaw.Value, 0f, 0f, 1f);

            if (MipmapsSupported)
            {
                OpenTK.Graphics.OpenGL.GL.GetInteger(OpenTK.Graphics.OpenGL.GetPName.Viewport, Viewport);
                OpenTK.Graphics.OpenGL.GL.GetFloat(OpenTK.Graphics.OpenGL.GetPName.ModelviewMatrix, out ModelMatrix);
                OpenTK.Graphics.OpenGL.GL.GetFloat(OpenTK.Graphics.OpenGL.GetPName.ProjectionMatrix, out ProjectionMatrix);
            }

            if (picking)
            {
                RenderObjects(RenderPass.Picking);
            }
            else
            {
                RenderObjects(RenderPass.Simple);
                RenderObjects(RenderPass.Alpha);
                RenderText();
                //RenderAvatar();
            }

            // Pop the world matrix
            OpenTK.Graphics.OpenGL.GL.PopMatrix();

            OpenTK.Graphics.OpenGL.GL.DisableClientState(OpenTK.Graphics.OpenGL.ArrayCap.TextureCoordArray);
            OpenTK.Graphics.OpenGL.GL.DisableClientState(OpenTK.Graphics.OpenGL.ArrayCap.VertexArray);
            OpenTK.Graphics.OpenGL.GL.DisableClientState(OpenTK.Graphics.OpenGL.ArrayCap.NormalArray);

            OpenTK.Graphics.OpenGL.GL.Flush();
        }

        //protected override void OnRenderFrame(FrameEventArgs e)
        //{
        //    //base.OnRenderFrame(e);

        //    if (viewport_changed)
        //    {
        //        viewport_changed = false;

        //        OpenTK.Graphics.OpenGL.GL.Viewport(0, 0, Width, Height);

        //        OpenTK.Matrix4 ortho_projection = OpenTK.Matrix4.CreateOrthographicOffCenter(0, Width, Height, 0, -1, 1);
        //        OpenTK.Graphics.OpenGL.GL.MatrixMode(OpenTK.Graphics.OpenGL.MatrixMode.Projection);
        //        OpenTK.Graphics.OpenGL.GL.LoadMatrix(ref ortho_projection);
        //    }

        //    OpenTK.Graphics.OpenGL.GL.Clear(OpenTK.Graphics.OpenGL.ClearBufferMask.ColorBufferBit);

        //    OpenTK.Graphics.OpenGL.GL.Begin(OpenTK.Graphics.OpenGL.BeginMode.Quads);

        //    OpenTK.Graphics.OpenGL.GL.TexCoord2(0, 0); OpenTK.Graphics.OpenGL.GL.Vertex2(0, 0);
        //    OpenTK.Graphics.OpenGL.GL.TexCoord2(1, 0); OpenTK.Graphics.OpenGL.GL.Vertex2(TextBitmap.Width, 0);
        //    OpenTK.Graphics.OpenGL.GL.TexCoord2(1, 1); OpenTK.Graphics.OpenGL.GL.Vertex2(TextBitmap.Width, TextBitmap.Height);
        //    OpenTK.Graphics.OpenGL.GL.TexCoord2(0, 1); OpenTK.Graphics.OpenGL.GL.Vertex2(0, TextBitmap.Height);

        //    OpenTK.Graphics.OpenGL.GL.End();

        //    glControl.SwapBuffers();
        //}

        private static void GluPerspective(float fovy, float aspect, float zNear, float zFar)
        {
            float fH = (float)Math.Tan(fovy / 360 * (float)Math.PI) * zNear;
            float fW = fH * aspect;
            OpenTK.Graphics.OpenGL.GL.Frustum(-fW, fW, -fH, fH, zNear, zFar);
        }

        private bool TryPick(int x, int y, out FacetedMesh picked, out int faceID)
        {
            // Save old attributes
            OpenTK.Graphics.OpenGL.GL.PushAttrib(OpenTK.Graphics.OpenGL.AttribMask.AllAttribBits);

            // Disable some attributes to make the objects flat / solid color when they are drawn
            OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.Fog);
            OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.Texture2D);
            OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.Dither);
            OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.Lighting);
            OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.LineStipple);
            OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.PolygonStipple);
            OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.CullFace);
            OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.Blend);
            OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.AlphaTest);

            Render(true);

            byte[] color = new byte[4];
            OpenTK.Graphics.OpenGL.GL.ReadPixels(x, glControl.Height - y, 1, 1, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, OpenTK.Graphics.OpenGL.PixelType.UnsignedByte, color);

            OpenTK.Graphics.OpenGL.GL.PopAttrib();

            int primID = Utils.BytesToUInt16(color, 0);
            faceID = color[2];

            picked = null;

            lock (Prims)
            {
                foreach (var mesh in Prims.Values)
                {
                    foreach (var face in mesh.Faces)
                    {
                        if (((FaceData)face.UserData).PickingID == primID)
                        {
                            picked = mesh;
                            break;
                        }
                    }

                    if (picked != null) break;
                }
            }

            return picked != null;
        }

        //private void ShowAvatar(Avatar av)
        //{
        //    GLAvatar ga = new GLAvatar();

        //    RenderAvatar ra = new Rendering.RenderAvatar();
        //    ra.avatar = av;
        //    ra.glavatar = ga;
        //    updateAVtes(ra);
        //    ra.glavatar.morph(av);
        //}

        private void UpdatePrimBlocking(Primitive prim)
        {

            FacetedMesh mesh = null;
            FacetedMesh existingMesh = null;

            lock (Prims)
            {
                if (Prims.ContainsKey(prim.LocalID))
                {
                    existingMesh = Prims[prim.LocalID];
                }
            }

            if (prim.Textures == null)
                return;

            //if (prim.PrimData.PCode == PCode.Avatar)
            //{
            //    ShowAvatar(client.Network.CurrentSim.ObjectsAvatars[prim.LocalID]);
            //}

            try
            {
                if (prim.Sculpt != null && prim.Sculpt.SculptTexture != UUID.Zero)
                {
                    if (prim.Sculpt.Type != SculptType.Mesh)
                    {
                        // Regular sculptie
                        Image img = null;
                        if (!LoadTexture(prim.Sculpt.SculptTexture, ref img, true))
                            return;
                        mesh = renderer.GenerateFacetedSculptMesh(prim, (Bitmap)img, DetailLevel.Highest);
                    }
                    else
                    {
                        // Mesh
                        AutoResetEvent gotMesh = new AutoResetEvent(false);
                        bool meshSuccess = false;

                        client.Assets.RequestMesh(prim.Sculpt.SculptTexture, (success, meshAsset) =>
                        {
                            if (!success || !meshAsset.Decode())
                            {
                                Logger.Log("Failed to fetch or decode the mesh asset", Helpers.LogLevel.Warning, client);
                            }
                            else
                            {
                                mesh = GenerateFacetedMesh(prim, meshAsset.MeshData, DetailLevel.Highest);
                                meshSuccess = true;
                            }
                            gotMesh.Set();
                        });

                        if (!gotMesh.WaitOne(20 * 1000, false))
                        {
                            gotMesh.Close();
                            return;
                        }

                        if (!meshSuccess)
                        {
                            gotMesh.Close();
                            return;
                        }
                    }
                }
                else
                {
                    mesh = renderer.GenerateFacetedMesh(prim, DetailLevel.Highest);
                }
            }
            catch
            {
                return;
            }

            // Create a FaceData struct for each face that stores the 3D data
            // in a OpenGL friendly format
            for (int j = 0; j < mesh.Faces.Count; j++)
            {
                Face face = mesh.Faces[j];
                FaceData data = new FaceData();

                // Vertices for this face
                data.Vertices = new float[face.Vertices.Count * 3];
                data.Normals = new float[face.Vertices.Count * 3];
                for (int k = 0; k < face.Vertices.Count; k++)
                {
                    data.Vertices[k * 3 + 0] = face.Vertices[k].Position.X;
                    data.Vertices[k * 3 + 1] = face.Vertices[k].Position.Y;
                    data.Vertices[k * 3 + 2] = face.Vertices[k].Position.Z;

                    data.Normals[k * 3 + 0] = face.Vertices[k].Normal.X;
                    data.Normals[k * 3 + 1] = face.Vertices[k].Normal.Y;
                    data.Normals[k * 3 + 2] = face.Vertices[k].Normal.Z;
                }

                // Indices for this face
                data.Indices = face.Indices.ToArray();

                // Texture transform for this face
                Primitive.TextureEntryFace teFace = prim.Textures.GetFace((uint)j);

                if (teFace != null) renderer.TransformTexCoords(face.Vertices, face.Center, teFace, mesh.Prim.Scale);

                // Texcoords for this face
                data.TexCoords = new float[face.Vertices.Count * 2];
                for (int k = 0; k < face.Vertices.Count; k++)
                {
                    data.TexCoords[k * 2 + 0] = face.Vertices[k].TexCoord.X;
                    data.TexCoords[k * 2 + 1] = face.Vertices[k].TexCoord.Y;
                }

                // Set the UserData for this face to our FaceData struct
                face.UserData = data;
                mesh.Faces[j] = face;


                if (existingMesh != null &&
                    j < existingMesh.Faces.Count &&
                    existingMesh.Faces[j].TextureFace.TextureID == teFace.TextureID &&
                    ((FaceData)existingMesh.Faces[j].UserData).TexturePointer != 0
                    )
                {
                    FaceData existingData = (FaceData)existingMesh.Faces[j].UserData;
                    data.TexturePointer = existingData.TexturePointer;
                }
                else
                {

                    var textureItem = new TextureLoadItem()
                    {
                        Data = data,
                        Prim = prim,
                        TeFace = teFace
                    };

                    PendingTextures.Enqueue(textureItem);
                }
            }

            lock (Prims)
            {
                Prims[prim.LocalID] = mesh;
            }

            mesh = null;
            existingMesh = null;

            GLInvalidate();
        }

        private bool LoadTexture(UUID textureID, ref Image texture, bool removeAlpha)
        {
            ManualResetEvent gotImage = new ManualResetEvent(false);
            Image img = null;

            if (Textures.ContainsKey(textureID))
            {
                texture = Textures[textureID];
                return true;
            }

            try
            {
                gotImage.Reset();
                instance.Client.Assets.RequestImage(textureID, (TextureRequestState state, AssetTexture assetTexture) =>
                {
                    if (state == TextureRequestState.Finished)
                    {
                        ManagedImage mi;
                        OpenJPEG.DecodeToImage(assetTexture.AssetData, out mi);

                        if (removeAlpha)
                        {
                            if ((mi.Channels & ManagedImage.ImageChannels.Alpha) != 0)
                            {
                                mi.ConvertChannels(mi.Channels & ~ManagedImage.ImageChannels.Alpha);
                            }
                        }

                        MemoryStream mem = new MemoryStream(mi.ExportTGA());
                        img = LoadTGAClass.LoadTGA(mem);

                        Textures[textureID] = (System.Drawing.Image)img.Clone();

                        mem.Dispose();
                    }

                    gotImage.Set();
                }
                );

                gotImage.WaitOne(30 * 1000, false);

                gotImage.Close();

                if (img != null)
                {
                    texture = img;
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                Logger.Log(e.Message, Helpers.LogLevel.Error, instance.Client, e);
                return false;
            }
        }

        private void GLInvalidate()
        {
            if (glControl == null || !RenderingEnabled) return;

            if (InvokeRequired)
            {
                if (IsHandleCreated)
                {
                    BeginInvoke(new MethodInvoker(() => GLInvalidate()));
                }
                return;
            }

            glControl.Invalidate();
        }
        #endregion Private methods (the meat)

        #region Form controls handlers
        private void scroll_ValueChanged(object sender, EventArgs e)
        {
            GLInvalidate();
        }

        private void scrollZoom_ValueChanged(object sender, EventArgs e)
        {
            glControl_Resize(null, null);
            GLInvalidate();
        }

        private void btnReset_Click(object sender, EventArgs e)
        {
            scrollYaw.Value = 90;
            scrollPitch.Value = 0;
            scrollRoll.Value = 0;
            scrollZoom.Value = -30;
            Center = OpenMetaverse.Vector3.Zero;

            GLInvalidate();
        }

        private void oBJToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "OBJ files (*.obj)|*.obj";

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                if (!MeshToOBJ.MeshesToOBJ(Prims, dialog.FileName))
                {
                    MessageBox.Show("Failed to save file " + dialog.FileName +
                        ". Ensure that you have permission to write to that file and it is currently not in use");
                }
            }

            dialog.Dispose();
        }

        private void cbAA_CheckedChanged(object sender, EventArgs e)
        {
            //instance.GlobalSettings["use_multi_sampling"] = UseMultiSampling = cbAA.Checked;
            //SetupGLControl();
        }

        #endregion Form controls handlers

        #region Context menu
        private void ctxObjects_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (instance.State.IsSitting)
            {
                sitToolStripMenuItem.Text = "Stand up";
            }
            else if (RightclickedPrim.Prim.Properties != null
                && !string.IsNullOrEmpty(RightclickedPrim.Prim.Properties.SitName))
            {
                sitToolStripMenuItem.Text = RightclickedPrim.Prim.Properties.SitName;
            }
            else
            {
                sitToolStripMenuItem.Text = "Sit";
            }

            if (!isobject)
            {
                sitToolStripMenuItem.Enabled = false;
            }
            else
            {
                sitToolStripMenuItem.Enabled = true;
            }

            if (RightclickedPrim.Prim.Properties != null
                && !string.IsNullOrEmpty(RightclickedPrim.Prim.Properties.TouchName))
            {
                touchToolStripMenuItem.Text = RightclickedPrim.Prim.Properties.TouchName;
            }
            else
            {
                touchToolStripMenuItem.Text = "Touch";
            }

            if ((RightclickedPrim.Prim.Flags & PrimFlags.Touch) == PrimFlags.Touch)
            {
                touchToolStripMenuItem.Enabled = true;
            }
            else
            {
                touchToolStripMenuItem.Enabled = false;
            }

            if ((RightclickedPrim.Prim.Flags & PrimFlags.Money) == PrimFlags.Money)
            {
                if (RightclickedPrim.Prim.Properties != null
                && !string.IsNullOrEmpty(RightclickedPrim.Prim.Properties.TouchName))
                {
                    payBuyToolStripMenuItem.Text = RightclickedPrim.Prim.Properties.TouchName;
                }
                else
                {
                    payBuyToolStripMenuItem.Text = "Pay/Buy";
                }

                payBuyToolStripMenuItem.Enabled = true;
            }

            if (!isobject)
            {
                payBuyToolStripMenuItem.Enabled = false;
            }

            if (isobject)
            {
                if (RightclickedPrim.Prim.Properties != null)
                {
                    if (RightclickedPrim.Prim.Properties.OwnerID == client.Self.AgentID)
                    {
                        takeToolStripMenuItem.Enabled = true;
                        deleteToolStripMenuItem.Enabled = true;
                    }
                    else
                    {
                        takeToolStripMenuItem.Enabled = false;
                        deleteToolStripMenuItem.Enabled = false;
                    }
                }
            }
            else
            {
                takeToolStripMenuItem.Enabled = false;
                deleteToolStripMenuItem.Enabled = false;
                returnToolStripMenuItem.Enabled = false;
            }
        }

        private void touchToolStripMenuItem_Click(object sender, EventArgs e)
        {

            client.Self.Grab(RightclickedPrim.Prim.LocalID, OpenMetaverse.Vector3.Zero, OpenMetaverse.Vector3.Zero, OpenMetaverse.Vector3.Zero, RightclickedFaceID, OpenMetaverse.Vector3.Zero, OpenMetaverse.Vector3.Zero, OpenMetaverse.Vector3.Zero);
            Thread.Sleep(100);
            client.Self.DeGrab(RightclickedPrim.Prim.LocalID);
        }

        private void sitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!instance.State.IsSitting)
            {
                instance.State.SetSitting(true, RightclickedPrim.Prim.ID);
            }
            else
            {
                instance.State.SetSitting(false, UUID.Zero);
            }
        }

        private void takeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            client.Inventory.RequestDeRezToInventory(RightclickedPrim.Prim.LocalID);
            Close();
        }

        private void returnToolStripMenuItem_Click(object sender, EventArgs e)
        {
            client.Inventory.RequestDeRezToInventory(RightclickedPrim.Prim.LocalID, DeRezDestination.ReturnToOwner, UUID.Zero, UUID.Random());
            Close();
        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (RightclickedPrim.Prim.Properties != null && RightclickedPrim.Prim.Properties.OwnerID != client.Self.AgentID)
                returnToolStripMenuItem_Click(sender, e);
            else
            {
                client.Inventory.RequestDeRezToInventory(RightclickedPrim.Prim.LocalID, DeRezDestination.AgentInventoryTake, client.Inventory.FindFolderForType(AssetType.TrashFolder), UUID.Random());
            }

            Close();
        }
        #endregion Context menu

        private void scrollYaw_Scroll(object sender, ScrollEventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            //TakeScreenShot = true;
            //snapped = false;

            SoundPlayer simpleSound = new SoundPlayer(Properties.Resources.camera_clic_with_flash);
            simpleSound.Play();
            simpleSound.Dispose();

            getScreehShot();
        }

        private void capScreenBeforeNextSwap()
        {
            snapped = false;
            TakeScreenShot = false;

            //Bitmap bmp = GrabScreenshot();
            //Image img = (Image)bmp;

            Bitmap newbmp = new Bitmap(glControl.Width, glControl.Height);
            Bitmap bmp = newbmp;

            System.Drawing.Imaging.BitmapData data = bmp.LockBits(glControl.ClientRectangle, System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            OpenTK.Graphics.OpenGL.GL.ReadPixels(0, 0, glControl.Width, glControl.Height, OpenTK.Graphics.OpenGL.PixelFormat.Bgr, OpenTK.Graphics.OpenGL.PixelType.UnsignedByte, data.Scan0);

            OpenTK.Graphics.OpenGL.GL.Finish();

            bmp.UnlockBits(data);
            bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);

            string path = METAbolt.DataFolder.GetDataFolder() ;
            string filename = "Object_Snaphot_" + DateTime.Now.ToString() + ".png";
            filename = filename.Replace("/", "-");
            filename = filename.Replace(":", "-");

            saveFileDialog1.Filter = "PNG files (*.png)|*.png";
            saveFileDialog1.FilterIndex = 1;
            saveFileDialog1.FileName = filename;
            saveFileDialog1.InitialDirectory = path;

            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                bmp.Save(saveFileDialog1.FileName, ImageFormat.Png);
            }

            newbmp.Dispose();
            //bmp.Dispose();  
        }

        private void getScreehShot()
        {
            //snapped = false;
            //TakeScreenShot = false;

            //Bitmap bmp = GrabScreenshot();
            //Image img = (Image)bmp;

            Bitmap newbmp = new Bitmap(glControl.Width, glControl.Height);
            Bitmap bmp = newbmp;

            //System.Drawing.Imaging.BitmapData data = bmp.LockBits(glControl.ClientRectangle, System.Drawing.Imaging.ImageLockMode.WriteOnly,
            //    System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            System.Drawing.Imaging.BitmapData data = data = bmp.LockBits(glControl.ClientRectangle, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            OpenTK.Graphics.OpenGL.GL.ReadPixels(0, 0, glControl.Width, glControl.Height, OpenTK.Graphics.OpenGL.PixelFormat.Bgra, OpenTK.Graphics.OpenGL.PixelType.UnsignedByte, data.Scan0);

            OpenTK.Graphics.OpenGL.GL.Finish();

            bmp.UnlockBits(data);
            bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);

            string path = METAbolt.DataFolder.GetDataFolder() ;
            string filename = "Object_Snaphot_" + DateTime.Now.ToString() + ".png";
            filename = filename.Replace("/", "-");
            filename = filename.Replace(":", "-");

            saveFileDialog1.Filter = "PNG files (*.png)|*.png";
            saveFileDialog1.FilterIndex = 1;
            saveFileDialog1.FileName = filename;
            saveFileDialog1.InitialDirectory = path;

            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                bmp.Save(saveFileDialog1.FileName, ImageFormat.Png);
            }

            newbmp.Dispose();
        }

        //public Bitmap GrabScreenshot()
        //{
        //    Bitmap newbmp = new Bitmap(glControl.Width, glControl.Height);
        //    Bitmap bmp = newbmp;

        //    System.Drawing.Imaging.BitmapData data = bmp.LockBits(glControl.ClientRectangle, System.Drawing.Imaging.ImageLockMode.WriteOnly,
        //        System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        //    OpenTK.Graphics.OpenGL.GL.ReadPixels(0, 0, glControl.Width, glControl.Height, OpenTK.Graphics.OpenGL.PixelFormat.Bgr, OpenTK.Graphics.OpenGL.PixelType.UnsignedByte, data.Scan0);

        //    OpenTK.Graphics.OpenGL.GL.Finish();

        //    bmp.UnlockBits(data);
        //    bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);

        //    newbmp.Dispose(); 

        //    return bmp;
        //}

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void picAutoSit_MouseHover(object sender, EventArgs e)
        {
            toolTip.Show(picAutoSit);
        }

        private void picAutoSit_MouseLeave(object sender, EventArgs e)
        {
            toolTip.Close();
        }

        private void picAutoSit_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start(@"http://www.metabolt.net/METAwiki/META3D.ashx");
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            clearcolour = Color.RoyalBlue;
            OpenTK.Graphics.OpenGL.GL.ClearColor(clearcolour);
            GLInvalidate();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            clearcolour = Color.White;
            OpenTK.Graphics.OpenGL.GL.ClearColor(clearcolour);
            GLInvalidate();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            clearcolour = Color.Black;
            OpenTK.Graphics.OpenGL.GL.ClearColor(clearcolour);
            GLInvalidate();
        }

        private void button5_Click(object sender, EventArgs e)
        {
            clearcolour = Color.Red;
            OpenTK.Graphics.OpenGL.GL.ClearColor(clearcolour);
            GLInvalidate();
        }

        private void button6_Click(object sender, EventArgs e)
        {
            clearcolour = Color.Green;
            OpenTK.Graphics.OpenGL.GL.ClearColor(clearcolour);
            GLInvalidate();
        }

        private void button7_Click(object sender, EventArgs e)
        {
            clearcolour = Color.Yellow;
            OpenTK.Graphics.OpenGL.GL.ClearColor(clearcolour);
            GLInvalidate();
        }

        private void payBuyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Primitive sPr = new Primitive();
            sPr = RightclickedPrim.Prim;

            if (sPr.Properties == null)
            {
                sPr = objectitem.Prim;

                if (sPr.Properties == null)
                {
                    payBuyToolStripMenuItem.Enabled = false;
                    return;
                }
            }

            SaleType styp = sPr.Properties.SaleType;

            //if (sprice > 0)
            //{
            if (styp != SaleType.Not)
            {
                int sprice = sPr.Properties.SalePrice;

                (new frmPay(instance, sPr.ID, sPr.Properties.Name, sprice, sPr)).Show(this);
            }
            else
            {
                //(new frmPay(instance, sPr.ID, sPr.Properties.Name)).Show(this);
                (new frmPay(instance, sPr.ID, string.Empty, sPr.Properties.Name, sPr)).Show(this);
            }
            //}
        }

        private void chkMipmaps_CheckedChanged(object sender, EventArgs e)
        {
            //enablemipmapd = chkMipmaps.Checked; 
        }

        private void button8_Click(object sender, EventArgs e)
        {
            clearcolour = Color.Transparent;
            //OpenTK.Graphics.OpenGL.GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.Blend);
            //OpenTK.Graphics.OpenGL.GL.BlendFunc(OpenTK.Graphics.OpenGL.BlendingFactorSrc.SrcAlpha, OpenTK.Graphics.OpenGL.BlendingFactorDest.OneMinusSrcAlpha);
            OpenTK.Graphics.OpenGL.GL.ClearColor(clearcolour);
            OpenTK.Graphics.OpenGL.GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.Blend);
            OpenTK.Graphics.OpenGL.GL.BlendFunc(OpenTK.Graphics.OpenGL.BlendingFactorSrc.SrcAlpha, OpenTK.Graphics.OpenGL.BlendingFactorDest.OneMinusSrcAlpha);
            GLInvalidate();
        }

        private void META3D_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                client.Objects.TerseObjectUpdate -= new EventHandler<TerseObjectUpdateEventArgs>(Objects_TerseObjectUpdate);
                client.Objects.ObjectUpdate -= new EventHandler<PrimEventArgs>(Objects_ObjectUpdate);
                client.Objects.ObjectDataBlockUpdate -= new EventHandler<ObjectDataBlockUpdateEventArgs>(Objects_ObjectDataBlockUpdate);
                client.Network.SimChanged -= new EventHandler<SimChangedEventArgs>(SIM_OnSimChanged);
                client.Self.TeleportProgress -= new EventHandler<TeleportEventArgs>(Self_TeleportProgress);

                lock (this.Prims)
                {
                    Prims.Clear();
                }

                lock (this.Textures)
                {
                    Textures.Clear();
                }
            }
            catch (Exception ex)
            {
                //string exp = ex.Message;
                reporter.Show(ex);
            }
        }

        private void button2_MouseHover(object sender, EventArgs e)
        {
            System.Windows.Forms.ToolTip ToolTip1 = new System.Windows.Forms.ToolTip();
            ToolTip1.SetToolTip(button2, "Blue background");
        }

        private void button3_MouseHover(object sender, EventArgs e)
        {
            System.Windows.Forms.ToolTip ToolTip1 = new System.Windows.Forms.ToolTip();
            ToolTip1.SetToolTip(button3, "White background");
        }

        private void button4_MouseHover(object sender, EventArgs e)
        {
            System.Windows.Forms.ToolTip ToolTip1 = new System.Windows.Forms.ToolTip();
            ToolTip1.SetToolTip(button4, "Black background");
        }

        private void button5_MouseHover(object sender, EventArgs e)
        {
            System.Windows.Forms.ToolTip ToolTip1 = new System.Windows.Forms.ToolTip();
            ToolTip1.SetToolTip(button5, "Red background");
        }

        private void button6_MouseHover(object sender, EventArgs e)
        {
            System.Windows.Forms.ToolTip ToolTip1 = new System.Windows.Forms.ToolTip();
            ToolTip1.SetToolTip(button6, "Green background");
        }

        private void button7_MouseHover(object sender, EventArgs e)
        {
            System.Windows.Forms.ToolTip ToolTip1 = new System.Windows.Forms.ToolTip();
            ToolTip1.SetToolTip(button7, "Yellow background");
        }

        private void button8_MouseHover(object sender, EventArgs e)
        {
            System.Windows.Forms.ToolTip ToolTip1 = new System.Windows.Forms.ToolTip();
            ToolTip1.SetToolTip(button8, "Transparent background");
        }

        private void button1_MouseHover(object sender, EventArgs e)
        {
            System.Windows.Forms.ToolTip ToolTip1 = new System.Windows.Forms.ToolTip();
            ToolTip1.SetToolTip(button1, "Take snapshot");
        }
    }
}