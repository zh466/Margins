using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Polya {
    sealed class LiveWebInk : Form {
        [StructLayout(LayoutKind.Sequential)]
        struct Blend {public byte Operation,Flags,Alpha,Format;}
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr window,IntPtr destination,ref Point position,
            ref Size size,IntPtr source,ref Point origin,int colorKey,ref Blend blend,int flags);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window,int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window,int index,int value);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr objectHandle);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        RasterBlitter.Image pixels;
        IntPtr dc=CreateCompatibleDC(IntPtr.Zero);
        Rectangle viewport,visible;
        readonly Surface surface=new Surface();
        readonly Board board=Board.Make("Web annotations","other");
        Workspace owner;
        float dpiScale=1;
        bool drawing,resourcesReleased;
        uint penId;
        int previousSampleTime;
        readonly Workspace.PenInfo[] history=new Workspace.PenInfo[64];
        public bool Drawing {get{return drawing;}}
        public bool CanUndo {get{return surface.CanUndo;}}
        public bool HasStrokes {get{return board.Marks.Count>0;}}
        internal bool Presented {get;private set;}
        public event Action<Mark[]> Commit;

        public LiveWebInk(Workspace workspace) {
            owner=workspace;
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;
            surface.Bind(board);
            surface.Changed+=()=>{if(Commit!=null)Commit(board.Marks.ToArray());};
        }
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {
            get {
                var p=base.CreateParams;p.ExStyle|=0x00080000|0x08000000|0x80;
                if(!drawing)p.ExStyle|=0x20;
                return p;
            }
        }
        protected override void WndProc(ref Message msg) {
            if(drawing&&HandlePen(ref msg))return;
            if(!drawing&&msg.Msg==0x84){msg.Result=new IntPtr(-1);return;}
            if(msg.Msg==0x21){msg.Result=new IntPtr(3);return;}
            base.WndProc(ref msg);
        }
        public void SetMode(bool enabled) {
            drawing=enabled;
            Cursor=enabled?Cursors.Cross:Cursors.Default;
            if(IsHandleCreated) {
                int style=GetWindowLong(Handle,-20);
                SetWindowLong(Handle,-20,enabled?style&~0x20:style|0x20);
            }
            Render();
        }
        public void LoadStrokes(Mark[] strokes,float[][] legacy) {
            surface.Forget(board);
            board.Marks=new List<Mark>(strokes??new Mark[0]);
            if(strokes==null&&legacy!=null)foreach(var line in legacy) {
                var points=new List<PointF>();
                for(int i=0;i+1<line.Length;i+=2)
                    points.Add(new PointF(line[i]*viewport.Width/dpiScale,line[i+1]*viewport.Height/dpiScale));
                if(points.Count>0)board.Marks.Add(Ink.Finish(points,"#5F43CD",2.6f,false));
            }
            surface.Bind(board);Render();
        }
        public void Undo() {surface.Undo(false);Render();}
        public void Place(Form window,Rectangle full,Rectangle clipped,float scale) {
            bool viewportChanged=viewport!=full||dpiScale!=scale;
            viewport=full;dpiScale=scale;surface.DeviceScale=scale;
            surface.Area=new RectangleF(0,0,full.Width/scale,full.Height/scale);
            if(clipped.Width<=0||clipped.Height<=0||!window.Visible||window.WindowState==FormWindowState.Minimized||
                (!drawing&&!HasStrokes)) {if(Visible)Hide();return;}
            bool changed=viewportChanged||visible!=clipped||!Visible;
            visible=clipped;
            if(!Visible)Show(window);
            if(changed)Render();
        }
        public void DrawOn(Graphics g,int width,int height) {
            if(surface.Area.Width<=0||surface.Area.Height<=0)return;
            var state=g.Save();
            g.ScaleTransform(width/surface.Area.Width,height/surface.Area.Height);
            surface.Draw(g,false);g.Restore(state);
        }
        void Render() {
            if(!Visible||visible.Width<=0||visible.Height<=0)return;
            if(!drawing&&!HasStrokes){Hide();return;}
            if(pixels==null||pixels.Bitmap.Size!=visible.Size) {
                if(pixels!=null)pixels.Dispose();
                pixels=new RasterBlitter.Image(visible.Width,visible.Height);
            }
            using(var g=Graphics.FromImage(pixels.Bitmap)) {
                g.Clear(drawing?Color.FromArgb(1,255,255,255):Color.Transparent);
                g.TranslateTransform(viewport.X-visible.X,viewport.Y-visible.Y);
                DrawOn(g,viewport.Width,viewport.Height);
            }
            if(dc==IntPtr.Zero||pixels.Handle==IntPtr.Zero)return;
            var old=SelectObject(dc,pixels.Handle);
            if(old==IntPtr.Zero||old==new IntPtr(-1))return;
            try {
                var dst=visible.Location;var size=visible.Size;var src=Point.Empty;
                var blend=new Blend{Alpha=255,Format=1};
                Presented=UpdateLayeredWindow(Handle,IntPtr.Zero,ref dst,ref size,dc,ref src,0,ref blend,2);
            } finally {SelectObject(dc,old);}
        }
        PointF Relative(Point location) {return new PointF((location.X+visible.X-viewport.X)/dpiScale,(location.Y+visible.Y-viewport.Y)/dpiScale);}
        void Begin(Point location,bool inverted) {
            owner.CopyBrowserTool(surface,inverted);
            surface.OnDown(Relative(location),false,false);Render();
        }
        void End(Point location) {
            surface.OnUp(Relative(location),false);
            surface.PenContact=false;surface.PenPressure=1;Render();
        }
        bool HandlePen(ref Message message) {
            int id=(int)(message.WParam.ToInt64()&0xffff);
            if(message.Msg==Workspace.PointerCaptureChanged&&penId==(uint)id) {
                penId=0;surface.Cancel();surface.PenContact=false;Render();return true;
            }
            if(message.Msg!=Workspace.PointerDown&&message.Msg!=Workspace.PointerUpdate&&message.Msg!=Workspace.PointerUp)return false;
            Workspace.PenInfo info;
            if(!Workspace.GetPointerPenInfo((uint)id,out info))return false;
            var at=PointToClient(info.Pointer.Pixel);
            if(message.Msg==Workspace.PointerDown&&penId==0) {
                penId=(uint)id;previousSampleTime=info.Pointer.Time;surface.PenContact=true;
                surface.PenPressure=owner.BrowserPressure(info);
                Begin(at,owner.BrowserEraser(info));return true;
            }
            if((uint)id!=penId||penId==0)return false;
            if((info.Pointer.Flags&Workspace.Canceled)!=0){penId=0;surface.Cancel();surface.PenContact=false;Render();return true;}
            if(message.Msg==Workspace.PointerUp){penId=0;End(at);}
            else {
                uint count=(uint)history.Length;
                if(info.Pointer.Count>1&&info.Pointer.Count<=count&&Workspace.GetPointerPenInfoHistory((uint)id,ref count,history)) {
                    for(int i=(int)count-1;i>=0;i--)if(history[i].Pointer.Time>=previousSampleTime)TrackPen(history[i]);
                } else TrackPen(info);
                previousSampleTime=info.Pointer.Time;Render();
            }
            return true;
        }
        void TrackPen(Workspace.PenInfo info) {
            surface.PenPressure=owner.BrowserPressure(info);
            surface.Track(Relative(PointToClient(info.Pointer.Pixel)),false);
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            if(!drawing||e.Button!=MouseButtons.Left||penId!=0)return;
            Capture=true;Begin(e.Location,false);
        }
        protected override void OnMouseMove(MouseEventArgs e) {if(Capture&&penId==0){surface.Track(Relative(e.Location),false);Render();}}
        protected override void OnMouseUp(MouseEventArgs e) {
            if(!Capture||penId!=0)return;
            End(e.Location);Capture=false;
        }
        protected override void OnMouseCaptureChanged(EventArgs e) {
            base.OnMouseCaptureChanged(e);
            if(!Capture&&penId==0&&surface.Dragging){surface.Cancel();Render();}
        }
        protected override void Dispose(bool disposing) {
            if(disposing&&!resourcesReleased) {
                resourcesReleased=true;
                if(pixels!=null){pixels.Dispose();pixels=null;}
                surface.Cancel();surface.ReleaseRaster();
                if(dc!=IntPtr.Zero){DeleteDC(dc);dc=IntPtr.Zero;}
            }
            base.Dispose(disposing);
        }
    }
}
