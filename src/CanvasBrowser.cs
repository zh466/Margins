using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Polya {
    sealed partial class Workspace {
        Mark browserMark;
        Panel browserHost;
        bool resizingBrowser;
        Rectangle browserFloatingBounds;
        LiveWebInk browserInk;
        Point resizeOrigin;
        Rectangle resizeStart;
        BrowserChrome browserChrome;

        // Use the physical window's dimensions and the page magnification together.
        // Dividing the two retains its viewport size in CSS pixels.
        float BrowserContentScale() {
            if(browserMark==null||!browserMark.PinnedWeb)return 1;
            float nominal=browserMark.BrowserWidth??Math.Max(370,Math.Min(760,browserMark.W));
            return Math.Max(.025f,surface.Board.Zoom*browserMark.W/nominal);
        }
        sealed class BrowserChrome : IDisposable {
            public readonly WebView2 View;
            readonly Panel holder;
            readonly FlowLayoutPanel header;
            readonly Dictionary<Control,int> widths=new Dictionary<Control,int>();
            readonly Font originalHandleFont;
            readonly Control handle;
            Font scaledHandleFont;
            float lastScale;
            double requestedZoom;
            bool? lastPinned;
            string desiredMetrics,appliedMetrics;
            bool updatingMetrics,disposed;
            public BrowserChrome(Panel holder,FlowLayoutPanel header,WebView2 web,Control handle) {
                this.holder=holder;this.header=header;View=web;this.handle=handle;
                originalHandleFont=handle.Font;
                foreach(Control control in header.Controls)widths.Add(control,control.Width);
            }
            public void Apply(float pixels,float deviceScale,double pageZoom,bool pinned) {
                if(lastScale!=pixels) {
                    lastScale=pixels;
                    holder.SuspendLayout();header.SuspendLayout();
                    header.Height=Math.Max(3,(int)Math.Round(55*pixels));
                    foreach(var entry in widths) {
                        var control=entry.Key;
                        control.Width=Math.Max(1,(int)Math.Round(entry.Value*pixels));
                        control.Height=Math.Max(1,(int)Math.Round(35*pixels));
                        control.Margin=new Padding(Math.Max(0,(int)Math.Round(3*pixels)));
                        var button=control as SheetButton;
                        if(button!=null){button.DrawingScale=pixels;button.Invalidate();}
                    }
                    var font=new Font(originalHandleFont.FontFamily,Math.Max(1,originalHandleFont.Size*pixels),originalHandleFont.Style);
                    handle.Font=font;
                    if(scaledHandleFont!=null)scaledHandleFont.Dispose();
                    scaledHandleFont=font;
                    int border=Math.Max(1,(int)Math.Round(7*pixels));
                    foreach(var grip in holder.Controls.OfType<Panel>().Where(panel=>panel!=header)) {
                        if(grip.Dock==DockStyle.Left||grip.Dock==DockStyle.Right)grip.Width=border;
                        if(grip.Dock==DockStyle.Top||grip.Dock==DockStyle.Bottom)grip.Height=border;
                    }
                    header.ResumeLayout();holder.ResumeLayout();
                }
                var web=View.CoreWebView2;
                if(web==null)return;
                if(lastPinned!=pinned) {
                    web.Settings.IsZoomControlEnabled=!pinned;
                    lastPinned=pinned;
                }
                double nativeZoom=Math.Max(.25,Math.Min(5,pageZoom));
                if(Math.Abs(requestedZoom-nativeZoom)>=.0001) {
                    if(pinned||requestedZoom>0)View.ZoomFactor=nativeZoom;
                    requestedZoom=nativeZoom;
                }

                // WebView2 clamps its native zoom. Outside that range, preserve the CSS viewport
                // and apply the remaining scale in the Chromium compositor.
                if(pinned&&Math.Abs(nativeZoom-pageZoom)>=.0001) {
                    double ratio=pageZoom/nativeZoom;
                    var width=Math.Max(1,Math.Min(100000,(int)Math.Round(View.ClientSize.Width/(deviceScale*ratio))));
                    var height=Math.Max(1,Math.Min(100000,(int)Math.Round(View.ClientSize.Height/(deviceScale*ratio))));
                    desiredMetrics=String.Format(CultureInfo.InvariantCulture,
                        "{{\"width\":{0},\"height\":{1},\"deviceScaleFactor\":{3:R},\"mobile\":false,\"scale\":{2:R}",
                        width,height,ratio,deviceScale)+"}";
                } else desiredMetrics=null;
                if(!updatingMetrics&&desiredMetrics!=appliedMetrics)UpdateMetrics(web);
            }
            async void UpdateMetrics(CoreWebView2 web) {
                updatingMetrics=true;
                string next=desiredMetrics;
                try {
                    if(next==null)await web.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride","{}");
                    else await web.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",next);
                } catch(ArgumentException) {}
                catch(InvalidOperationException) {}
                catch(System.Runtime.InteropServices.COMException) {}
                if(disposed||holder.IsDisposed||!holder.IsHandleCreated)return;
                try {
                    holder.BeginInvoke(new Action(()=>{
                        if(disposed)return;
                        updatingMetrics=false;
                        appliedMetrics=next;
                        if(desiredMetrics!=appliedMetrics)UpdateMetrics(web);
                    }));
                } catch(InvalidOperationException) {}
            }
            public void Dispose() {
                disposed=true;
                if(scaledHandleFont!=null){scaledHandleFont.Dispose();scaledHandleFont=null;}
            }
        }

        Mark StoreBrowser(Mark mark,Action<Mark> change) {
            if(browserMark!=null)mark=browserMark;
            var next=mark.Copy();
            change(next);
            if(surface.Board.Marks.Contains(mark))surface.Replace(mark,next);
            else surface.Add(next);
            browserMark=next;surface.ActiveBrowserMark=next;
            return next;
        }
        RectangleF BrowserWorld(Rectangle bounds) {
            var origin=surface.World(Logical(bounds.Location));
            return new RectangleF(origin.X,origin.Y,bounds.Width/(scale*surface.Board.Zoom),bounds.Height/(scale*surface.Board.Zoom));
        }
        void PinBrowser(Mark mark,Rectangle bounds,Uri address) {
            var rect=BrowserWorld(bounds);
            float priorScale=BrowserContentScale();
            double pageZoom=browserMark!=null&&browserMark.PinnedWeb?(browserMark.BrowserPageZoom??1):(browserChrome==null?1:browserChrome.View.ZoomFactor);
            StoreBrowser(mark,m=>{
                m.PinnedWeb=true;m.X=rect.X;m.Y=rect.Y;m.W=rect.Width;m.H=rect.Height;
                m.BrowserWidth=bounds.Width/(scale*priorScale);m.BrowserHeight=bounds.Height/(scale*priorScale);
                m.BrowserPageZoom=pageZoom;m.Text=address.AbsoluteUri;
            });
            Invalidate();
        }
        Rectangle BrowserBounds() {
            var display=surface.ScreenRect(browserMark.Bounds);
            return new Rectangle((int)Math.Round(display.X*scale),(int)Math.Round(display.Y*scale),
                Math.Max(1,(int)Math.Round(display.Width*scale)),Math.Max(1,(int)Math.Round(display.Height*scale)));
        }
        internal void CopyBrowserTool(Surface target,bool inverted) {
            target.Tool=inverted?"eraser":Surface.IsEraser(surface.Tool)?surface.Tool:"pen";
            target.Color=surface.Color;target.Weight=surface.Weight;target.Opacity=surface.Opacity;
            target.Dash=surface.Dash;target.Fill=surface.Fill;target.Tidy=surface.Tidy;
        }
        internal float BrowserPressure(PenInfo sample) {
            return !tablet.Pressure||(sample.Mask&PressureMask)==0?1:.25f+.75f*Math.Min(1024,sample.Pressure)/1024f;
        }
        internal bool BrowserEraser(PenInfo sample) {return tablet.EraserEnd&&(sample.Flags&(Inverted|Eraser))!=0;}
        void AlignWebInk() {
            if(browserHost==null||browserInk==null||IsDisposed)return;
            var web=browserHost.Controls.OfType<WebView2>().FirstOrDefault();
            if(web==null||!web.Visible||canvasPanel!=null||popup!=null){browserInk.Hide();return;}
            var full=web.RectangleToScreen(web.ClientRectangle);
            var clip=Rectangle.Intersect(full,RectangleToScreen(ClientRectangle));
            browserInk.Place(this,full,clip,scale*BrowserContentScale());
        }
        protected override void OnLocationChanged(EventArgs e) {base.OnLocationChanged(e);AlignWebInk();}
        protected override void OnVisibleChanged(EventArgs e) {base.OnVisibleChanged(e);AlignWebInk();}
        void AddBrowserMoveHandle(Control handle,Panel holder,Mark mark,WebView2 browser) {
            handle.MouseDown+=(s,e)=>{
                if(e.Button!=MouseButtons.Left)return;
                resizingBrowser=true;resizeOrigin=handle.PointToScreen(e.Location);
                resizeStart=holder.Bounds;handle.Capture=true;
            };
            handle.MouseMove+=(s,e)=>{
                if(!resizingBrowser||!handle.Capture)return;
                var at=handle.PointToScreen(e.Location);
                holder.Location=new Point(resizeStart.X+at.X-resizeOrigin.X,resizeStart.Y+at.Y-resizeOrigin.Y);
                browserFloatingBounds=holder.Bounds;
                AlignWebInk();
            };
            handle.MouseCaptureChanged+=(s,e)=>{
                if(!resizingBrowser||handle.Capture)return;
                resizingBrowser=false;
                if(browserMark!=null&&browserMark.PinnedWeb&&holder.Bounds!=resizeStart)
                    PinBrowser(mark,holder.Bounds,browser.Source??new Uri(browserMark.Text));
                Invalidate();
            };
            handle.MouseUp+=(s,e)=>handle.Capture=false;
        }
        void AddBrowserGrip(Panel holder,Mark mark,WebView2 browser) {
            foreach(var dock in new[]{DockStyle.Top,DockStyle.Bottom,DockStyle.Left,DockStyle.Right})
                AddBrowserResizeHandle(holder,mark,browser,dock);
        }
        void AddBrowserResizeHandle(Panel holder,Mark mark,WebView2 browser,DockStyle edge) {
            bool vertical=edge==DockStyle.Top||edge==DockStyle.Bottom;
            var grip=new Panel{Dock=edge,Width=7,Height=7,Cursor=vertical?Cursors.SizeNS:Cursors.SizeWE,BackColor=Art.Line};
            grip.MouseDown+=(s,e)=>{
                if(e.Button!=MouseButtons.Left)return;
                resizingBrowser=true;resizeOrigin=grip.PointToScreen(e.Location);resizeStart=holder.Bounds;grip.Capture=true;
            };
            grip.MouseMove+=(s,e)=>{
                if(!resizingBrowser)return;
                var current=grip.PointToScreen(e.Location);
                var dx=current.X-resizeOrigin.X;var dy=current.Y-resizeOrigin.Y;
                int left=resizeStart.Left,right=resizeStart.Right,top=resizeStart.Top,bottom=resizeStart.Bottom;
                int minW=Math.Max(24,(int)(320*scale*BrowserContentScale())),minH=Math.Max(24,(int)(170*scale*BrowserContentScale()));
                switch(edge) {
                    case DockStyle.Left:left=Math.Min(right-minW,Math.Max(0,left+dx));break;
                    case DockStyle.Right:right=Math.Max(left+minW,Math.Min(ClientSize.Width,right+dx));break;
                    case DockStyle.Top:top=Math.Min(bottom-minH,Math.Max(0,top+dy));break;
                    case DockStyle.Bottom:bottom=Math.Max(top+minH,Math.Min(ClientSize.Height,bottom+dy));break;
                }
                holder.Bounds=Rectangle.FromLTRB(left,top,right,bottom);
                browserFloatingBounds=holder.Bounds;
                AlignWebInk();
            };
            grip.MouseCaptureChanged+=(s,e)=>{
                if(!resizingBrowser||grip.Capture)return;
                resizingBrowser=false;
                if(browserMark!=null&&browserMark.PinnedWeb)
                    PinBrowser(mark,holder.Bounds,browser.Source??new Uri(browserMark.Text));
                Invalidate();
            };
            grip.MouseUp+=(s,e)=>grip.Capture=false;
            holder.Controls.Add(grip);grip.BringToFront();
        }

        internal static void Stroke(Graphics g,Pen pen,IList<float> line,int w,int h) {
            if(line.Count==2) {Art.Dot(g,pen.Color,line[0]*w,line[1]*h,pen.Width/2);return;}
            if(line.Count<4)return;
            var points=new PointF[line.Count/2];
            for(int i=0;i<points.Length;i++)points[i]=new PointF(line[2*i]*w,line[2*i+1]*h);
            if(points.Length==2)g.DrawLine(pen,points[0],points[1]);else g.DrawCurve(pen,points,.4f);
        }

        protected override void OnDragEnter(DragEventArgs e) { OnDragOver(e); }
        protected override void OnDragOver(DragEventArgs e) {
            if(page!=ScreenPage.Board||presenting||e.Data==null)return;
            if((e.AllowedEffect&DragDropEffects.Copy)!=0&&(e.Data.GetDataPresent(DataFormats.FileDrop)||e.Data.GetDataPresent(DataFormats.UnicodeText)||e.Data.GetDataPresent("UniformResourceLocatorW")))
                e.Effect=DragDropEffects.Copy;
        }
        protected override async void OnDragDrop(DragEventArgs e) {
            if(page!=ScreenPage.Board||presenting||e.Data==null)return;
            var board=surface.Board;
            var center=surface.World(Logical(PointToClient(new Point(e.X,e.Y))));
            var paths=e.Data.GetData(DataFormats.FileDrop) as string[];
            if(paths!=null) {
                foreach(var file in paths.Take(12)) {
                    await AttachToBoard(file,board,center);
                    center=new PointF(center.X+26,center.Y+26);
                }
                return;
            }
            var value=e.Data.GetData(DataFormats.UnicodeText) as string;
            if(String.IsNullOrWhiteSpace(value)) {
                var url=e.Data.GetData("UniformResourceLocatorW") as MemoryStream;
                if(url!=null)value=System.Text.Encoding.Unicode.GetString(url.ToArray()).TrimEnd('\0');
            }
            Uri uri;
            if(value!=null&&Dialogs.Url(value.Trim(),out uri))surface.Add(Mark.Make("link",center.X,center.Y,285,94,uri.Host,uri.AbsoluteUri));
        }
        async Task AttachToBoard(string file,Board board,PointF center) {
            try {
                var mark=await Task.Run(()=>{
                    var info=new FileInfo(file);
                    if(!info.Exists||info.Length>16*1024*1024)throw new IOException("Поддерживаются файлы до 16 МБ.");
                    switch(info.Extension.ToLowerInvariant()) {
                        case ".png":case ".jpg":case ".jpeg":return LessonArt.Worksheet(info.FullName,center);
                    }
                    using(var input=info.Open(FileMode.Open,FileAccess.Read,FileShare.Read)) {
                        if(input.Length>16*1024*1024)throw new IOException("Поддерживаются файлы до 16 МБ.");
                        var bytes=new byte[(int)input.Length];
                        int got=0;
                        while(got<bytes.Length) {
                            int read=input.Read(bytes,got,bytes.Length-got);
                            if(read==0)throw new IOException("Файл изменился при чтении.");
                            got+=read;
                        }
                        return Mark.Make("attachment",center.X,center.Y,320,80,info.Name,Convert.ToBase64String(bytes));
                    }
                });
                if(!IsDisposed&&page==ScreenPage.Board&&surface.Board==board)surface.Add(mark);
            } catch(Exception ex) {if(!IsDisposed)Notice("Не удалось добавить файл. "+ex.Message);}
        }
        void SaveAttachment(Mark mark) {
            var destination=FileSheet.SaveAttachment(this,Path.GetFileName(mark.Title));
            if(destination==null)return;
            try {File.WriteAllBytes(destination,Convert.FromBase64String(mark.Text));}
            catch(Exception ex){Notice("Не удалось сохранить файл. "+ex.Message);}
        }
    }
}
