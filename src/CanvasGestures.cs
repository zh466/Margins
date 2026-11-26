using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;

namespace Polya {
    sealed partial class Surface {
        readonly Dictionary<Mark,List<float>> cuts=new Dictionary<Mark,List<float>>();
        readonly List<PointF> eraseTail=new List<PointF>(24);
        readonly List<float> pressures=new List<float>();
        readonly List<StrokeSlice> pressureSlices=new List<StrokeSlice>();
        int pressureSealed;
        RectangleF pixelDamage;
        public float PenPressure=1;
        public bool PenContact;
        public bool Erasing { get { return IsEraser(drag); } }
        bool HasTrail {get{return Erasing||drag=="laser";}}
        public static bool IsEraser(string tool) {return tool=="eraser"||tool=="pixel";}

        sealed class PagePreview {
            public readonly Bitmap Image;
            public PagePreview(string base64) {
                try {
                    using(var bytes=new MemoryStream(Convert.FromBase64String(base64)))
                    using(var source=System.Drawing.Image.FromStream(bytes)) {
                        if((long)source.Width*source.Height<=16000000)Image=new Bitmap(source);
                    }
                } catch(ArgumentException) {} catch(FormatException) {}
            }
        }
        static readonly ConditionalWeakTable<string,PagePreview> pagePreviews=new ConditionalWeakTable<string,PagePreview>();
        static void DrawPinnedPage(Graphics g,Mark mark,RectangleF dest) {
            var preview=pagePreviews.GetValue(mark.WebImage,value=>new PagePreview(value)).Image;
            if(preview==null)return;
            var state=g.Save();
            g.SetClip(dest,CombineMode.Intersect);
            g.TranslateTransform(dest.X,dest.Y);
            g.ScaleTransform(dest.Width/preview.Width,dest.Height/preview.Height);
            g.DrawImage(preview,new Rectangle(0,0,preview.Width,preview.Height));
            if(mark.WebInk!=null)using(var pen=Art.Pen(Art.Purple,2.6f))
                foreach(var line in mark.WebInk)Workspace.Stroke(g,pen,line,preview.Width,preview.Height);
            g.Restore(state);
        }

        void AddTrail(PointF end) {
            if(eraseTail.Count>0&&Ink.Distance(eraseTail[eraseTail.Count-1],end)*Board.Zoom<1)return;
            if(eraseTail.Count>=24)eraseTail.RemoveAt(0);
            eraseTail.Add(end);
        }
        RectangleF EraserWorldBounds() {
            if(eraseTail.Count==0) return RectangleF.Empty;
            var first=eraseTail[0];
            float l=first.X,r=l,t=first.Y,b=t;
            foreach(var p in eraseTail) {l=Math.Min(l,p.X);r=Math.Max(r,p.X);t=Math.Min(t,p.Y);b=Math.Max(b,p.Y);}
            var rect=RectangleF.FromLTRB(l,t,r,b);
            rect.Inflate(13/Board.Zoom,13/Board.Zoom);
            return rect;
        }
        RectangleF EraserBounds() {
            var bounds=EraserWorldBounds();
            if(bounds.IsEmpty)return bounds;
            return new RectangleF(Area.X+Board.ViewX+bounds.X*Board.Zoom,Area.Y+Board.ViewY+bounds.Y*Board.Zoom,
                bounds.Width*Board.Zoom,bounds.Height*Board.Zoom);
        }
        void DrawEraserTrail(Graphics g) {
            bool laser=drag=="laser";
            var tint=laser?System.Drawing.Color.FromArgb(204,216,54,75):System.Drawing.Color.FromArgb(122,103,108,116);
            if(eraseTail.Count==1&&laser)Art.Dot(g,tint,eraseTail[0].X,eraseTail[0].Y,3/Board.Zoom);
            if(eraseTail.Count<2)return;
            var samples=Smooth(eraseTail.ToArray());
            using(var pen=Art.Pen(tint,(laser?4.5f:5.2f)/Board.Zoom))DrawInk(g,pen,samples);

        }

        static void ApplyCuts(Graphics g,Mark mark,IList<float> strip,RectangleF view) {
            using(var path=new GraphicsPath(FillMode.Winding)) {
                for(int i=0;i<strip.Count;i+=5) {
                    float r=strip[i+4];
                    if(r<=0)continue;
                    float ax=mark.X+strip[i],ay=mark.Y+strip[i+1],bx=mark.X+strip[i+2],by=mark.Y+strip[i+3];
                    if(!RectangleF.FromLTRB(Math.Min(ax,bx)-r,Math.Min(ay,by)-r,Math.Max(ax,bx)+r,Math.Max(ay,by)+r).IntersectsWith(view))continue;
                    path.StartFigure();path.AddEllipse(ax-r,ay-r,2*r,2*r);
                    if(ax==bx&&ay==by)continue;
                    path.StartFigure();path.AddEllipse(bx-r,by-r,2*r,2*r);
                    float length=(float)Math.Sqrt((bx-ax)*(bx-ax)+(by-ay)*(by-ay));
                    float x=(by-ay)*r/length,y=(ax-bx)*r/length;
                    path.AddPolygon(new[]{new PointF(ax+x,ay+y),new PointF(bx+x,by+y),new PointF(bx-x,by-y),new PointF(ax-x,ay-y)});
                }
                if(path.PointCount>0) g.SetClip(path,CombineMode.Exclude);
            }
        }

        bool PixelChanged(RectangleF view) {
            foreach(var mark in index.Query(view)) {
                List<float> strip;
                if(!cuts.TryGetValue(mark,out strip))continue;
                for(int i=strip.Count-5;i>=0;i-=5) {
                    var r=strip[i+4];
                    if(RectangleF.FromLTRB(mark.X+Math.Min(strip[i],strip[i+2])-r,mark.Y+Math.Min(strip[i+1],strip[i+3])-r,
                        mark.X+Math.Max(strip[i],strip[i+2])+r,mark.Y+Math.Max(strip[i+1],strip[i+3])+r).IntersectsWith(view))return true;
                }
            }
            return false;
        }
        void DrawDuringErase(Graphics g,Mark mark,RectangleF view) {
            if(erased.Contains(mark)) {DrawFaded(g,mark,view);return;}
            List<float> strip;
            if(!cuts.TryGetValue(mark,out strip)) {DrawMark(g,mark,view);return;}
            var state=g.Save();
            ApplyCuts(g,mark,strip,view);DrawMark(g,mark,view);g.Restore(state);
        }
        void DrawFaded(Graphics g,Mark mark,RectangleF view) {
            if(mark.Kind=="text"&&(mark.Cuts==null||mark.Cuts.Length==0)) {
                DrawBoardText(g,mark,view,24);
                return;
            }
            var region=RectangleF.Intersect(view,VisualBounds(mark));
            if(region.Width<=0||region.Height<=0)return;
            float density=Math.Min(Board.Zoom*DeviceScale,2048/Math.Max(region.Width,region.Height));
            int w=Math.Max(1,(int)Math.Ceiling(region.Width*density)),h=Math.Max(1,(int)Math.Ceiling(region.Height*density));
            using(var bitmap=new Bitmap(w,h,PixelFormat.Format32bppPArgb))
            using(var image=Graphics.FromImage(bitmap))
            using(var effect=new ImageAttributes()) {
                Art.Prepare(image);
                image.ScaleTransform(density,density);image.TranslateTransform(-region.X,-region.Y);
                DrawMark(image,mark,view);
                effect.SetColorMatrix(new ColorMatrix{Matrix33=.24f});
                var state=g.Save();
                g.TranslateTransform(region.X,region.Y);g.ScaleTransform(1/density,1/density);
                g.DrawImage(bitmap,new Rectangle(0,0,w,h),0,0,w,h,GraphicsUnit.Pixel,effect);
                g.Restore(state);
            }
        }
        void DrawLivePressure(Graphics g,System.Drawing.Color color,RectangleF view) {
            const int chunk=64;
            int end=stroke.Count-1;
            while(end-pressureSealed>chunk) {
                var path=new GraphicsPath(FillMode.Winding);
                AddPressurePath(path,Weight,stroke,pressures,pressureSealed,pressureSealed+chunk,RectangleF.Empty,true);
                pressureSlices.Add(new StrokeSlice(path,Weight));
                pressureSealed+=chunk;
            }
            using(var path=new GraphicsPath(FillMode.Winding))
            using(var brush=new SolidBrush(color)) {
                foreach(var slice in pressureSlices)
                    if(slice.Bounds.IntersectsWith(view))path.AddPath(slice.Path,false);
                AddPressurePath(path,Weight,stroke,pressures,pressureSealed,end,view,true);
                if(path.PointCount>0)g.FillPath(brush,path);
            }
        }
        static void DrawPressure(Graphics g,System.Drawing.Color color,float width,IList<PointF> points,IList<float> force,RectangleF view) {
            using(var shape=new GraphicsPath(FillMode.Winding))
            using(var brush=new SolidBrush(color)) {
                AddPressurePath(shape,width,points,force,0,points.Count-1,view);
                if(shape.PointCount>0)g.FillPath(brush,shape);
            }
        }
        static PointF SmoothAt(IList<PointF> points,int i) {
            var current=points[i];
            if(i==0||i+1>=points.Count)return current;
            var before=points[i-1];var after=points[i+1];
            return new PointF((before.X+2*current.X+after.X)/4,(before.Y+2*current.Y+after.Y)/4);
        }
        static void AddPressurePath(GraphicsPath shape,float width,IList<PointF> points,IList<float> force,int start,int end,RectangleF view,bool smooth=false) {
            for(int i=start+1;i<=end;i++) {
                var a=smooth?SmoothAt(points,i-1):points[i-1];
                var b=smooth?SmoothAt(points,i):points[i];
                var bounds=RectangleF.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y));
                bounds.Inflate(width,width);
                if(!view.IsEmpty&&!bounds.IntersectsWith(view))continue;
                float ra=width*force[i-1]/2,rb=width*force[i]/2;
                float dx=b.X-a.X,dy=b.Y-a.Y,length=(float)Math.Sqrt(dx*dx+dy*dy);
                shape.StartFigure();shape.AddEllipse(a.X-ra,a.Y-ra,2*ra,2*ra);
                if(length<.001f)continue;
                float nx=dy/length,ny=-dx/length;
                shape.AddPolygon(new[]{new PointF(a.X+nx*ra,a.Y+ny*ra),new PointF(b.X+nx*rb,b.Y+ny*rb),
                    new PointF(b.X-nx*rb,b.Y-ny*rb),new PointF(a.X-nx*ra,a.Y-ny*ra)});
                shape.StartFigure();shape.AddEllipse(b.X-rb,b.Y-rb,2*rb,2*rb);
            }
        }
    }
}
