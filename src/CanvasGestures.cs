using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Polya {
    sealed partial class Surface {
        readonly Dictionary<Mark,List<float>> cuts=new Dictionary<Mark,List<float>>();
        readonly List<PointF> eraseTail=new List<PointF>(24);
        readonly List<float> pressures=new List<float>();
        public float PenPressure=1;
        public bool PenContact;
        public bool Erasing { get { return IsEraser(drag); } }
        public static bool IsEraser(string tool) {return tool=="eraser"||tool=="pixel";}

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
            if(eraseTail.Count<2)return;
            var samples=Smooth(eraseTail.ToArray());
            for(int i=1;i<samples.Length;i++) {
                float amount=i/(float)(samples.Length-1);
                using(var pen=Art.Pen(System.Drawing.Color.FromArgb((int)(130*amount),107,110,122),(2+5*amount)/Board.Zoom))
                    g.DrawLine(pen,samples[i-1],samples[i]);
            }
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

        void DrawDuringErase(Graphics g,Mark mark,RectangleF view) {
            if(erased.Contains(mark)) {DrawFaded(g,mark,view);return;}
            List<float> strip;
            if(!cuts.TryGetValue(mark,out strip)) {DrawMark(g,mark,view);return;}
            var state=g.Save();
            ApplyCuts(g,mark,strip,view);DrawMark(g,mark,view);g.Restore(state);
        }
        void DrawFaded(Graphics g,Mark mark,RectangleF view) {
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
        static void DrawPressure(Graphics g,System.Drawing.Color color,float width,IList<PointF> points,IList<float> force,RectangleF view) {
            using(var shape=new GraphicsPath(FillMode.Winding))
            using(var brush=new SolidBrush(color)) {
                for(int i=1;i<points.Count;i++) {
                    var a=points[i-1];var b=points[i];
                    var bounds=RectangleF.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y));
                    bounds.Inflate(width,width);
                    if(!bounds.IntersectsWith(view))continue;
                    float ra=width*force[i-1]/2,rb=width*force[i]/2;
                    float dx=b.X-a.X,dy=b.Y-a.Y,length=(float)Math.Sqrt(dx*dx+dy*dy);
                    shape.StartFigure();shape.AddEllipse(a.X-ra,a.Y-ra,2*ra,2*ra);
                    if(length<.001f)continue;
                    float nx=dy/length,ny=-dx/length;
                    shape.AddPolygon(new[]{new PointF(a.X+nx*ra,a.Y+ny*ra),new PointF(b.X+nx*rb,b.Y+ny*rb),
                        new PointF(b.X-nx*rb,b.Y-ny*rb),new PointF(a.X-nx*ra,a.Y-ny*ra)});
                    shape.StartFigure();shape.AddEllipse(b.X-rb,b.Y-rb,2*rb,2*rb);
                }
                if(shape.PointCount>0)g.FillPath(brush,shape);
            }
        }
    }
}
