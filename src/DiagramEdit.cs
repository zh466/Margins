using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Polya {
    static class DiagramEdit {
        static readonly Color Construction=Art.C("#167F76");
        static readonly Dictionary<string,float[]> Defaults=MakeDefaults();
        static Dictionary<string,float[]> MakeDefaults() {
            var map=new Dictionary<string,float[]>();
            foreach(string kind in new[]{"triangle","altitude","median","midline","bisector","inscribed","circumcircle"})
                map[kind]=new[]{108f,25,46,121,218,121};
            map["right-triangle"]=new[]{62f,29,62,121,207,121};
            map["iso"]=new[]{130f,26,50,121,210,121};
            map["equilateral"]=new[]{130f,23,72,123.46f,188,123.46f};
            map["square"]=new[]{82f,26,178,26,178,122,82,122};
            map["rectangle"]=new[]{45f,31,215,31,215,119,45,119};
            map["rhombus"]=new[]{130f,22,216,74,130,126,44,74};
            map["parallelogram"]=new[]{81f,33,220,33,179,121,40,121};
            map["trapezoid"]=new[]{86f,26,171,26,218,122,42,122};
            map["polygon"]=new[]{105f,27,155,27,183,75,155,123,105,123,77,75};
            foreach(string kind in new[]{"circle","sector","tangent"})map[kind]=new[]{126f,75,173,61};
            map["sector"]=new[]{126f,75,169,53,155,113};
            foreach(string kind in new[]{"cube","box","section"})map[kind]=new[]{50f,119,kind=="box"?173:139,119,50,49,86,94};
            map["pyramid"]=new[]{40f,109,140,127,217,96,117,78,124,16};
            map["tetrahedron"]=new[]{46f,109,149,128,220,93,128,15};
            map["prism"]=new[]{54f,110,157,125,211,90,54,37};
            map["cylinder"]=new[]{130f,35,130,118,185,35};
            map["cone"]=new[]{130f,21,130,118,185,118};
            map["frustum"]=new[]{130f,32,130,118,186,118,162,32};
            map["sphere"]=new[]{130f,74,185,74};
            map["angles"]=new[]{35f,49,219,49,35,109,219,109,82,130,167,19};
            map["lever"]=new[]{41f,71,223,71,131,71};
            map["rays"]=new[]{130f,107,57,37};
            map["pendulum"]=new[]{121f,25,176,112};
            map["projectile"]=new[]{35f,126,132,25,223,126};
            map["forces"]=new[]{37f,124,215,52,127,89};
            return map;
        }
        public static bool IsCircuit(Mark m) {return m!=null&&m.Kind=="diagram"&&(m.Text=="circuit"||m.Text=="parallel");}
        static float[] Coordinates(Mark m) {
            float[] defaults;
            return m!=null&&m.Kind=="diagram"&&Defaults.TryGetValue(m.Text??"",out defaults)?m.DiagramPoints??defaults:null;
        }
        public static bool HasPoints(Mark m) {return Coordinates(m)!=null;}
        public static void Validate(Mark mark) {
            float[] baseline;
            if(mark.DiagramPoints!=null&&(!Defaults.TryGetValue(mark.Text??"",out baseline)||mark.DiagramPoints.Length!=baseline.Length||
                mark.DiagramPoints.Any(n=>Single.IsNaN(n)||n<0||n>260))) throw new FormatException("В файле есть некорректный чертёж.");
            if(Single.IsNaN(mark.Rotation)||Single.IsInfinity(mark.Rotation))mark.Rotation=0;
            mark.Rotation%=360;
            if(IsCircuit(mark)) Circuits.Validate(mark);
        }
        struct View {
            public float X,Y,S,Cos,Sin;
            public View(Mark m) {
                double radians=m.Rotation*Math.PI/180;
                Cos=(float)Math.Cos(radians);Sin=(float)Math.Sin(radians);
                X=m.X+m.W/2;Y=m.Y+m.H/2;
                S=Math.Max(.001f,Math.Min((m.W-12)/(260*Math.Abs(Cos)+150*Math.Abs(Sin)),(m.H-12)/(260*Math.Abs(Sin)+150*Math.Abs(Cos))));
            }
            public PointF ToWorld(float x,float y) {
                return new PointF(X+S*((x-130)*Cos-(y-75)*Sin),Y+S*((x-130)*Sin+(y-75)*Cos));
            }
            public PointF ToLocal(PointF p) {
                return new PointF(130+((p.X-X)*Cos+(p.Y-Y)*Sin)/S,75+(-(p.X-X)*Sin+(p.Y-Y)*Cos)/S);
            }
        }
        public static int HandleAt(Mark m,PointF world,float tolerance) {
            var points=Coordinates(m);if(points==null)return -1;
            var view=new View(m);
            for(int i=0;i<points.Length;i+=2) if(Ink.Distance(world,view.ToWorld(points[i],points[i+1]))<=tolerance)return i;
            return -1;
        }
        public static PointF HandlePosition(Mark m,int index) {
            var p=Coordinates(m);return new View(m).ToWorld(p[index],p[index+1]);
        }
        public static void BeginMove(Mark ghost) {ghost.DiagramPoints=(float[])Coordinates(ghost).Clone();}
        public static void Move(Mark ghost,int index,PointF world,bool snap) {
            var at=new View(ghost).ToLocal(world);
            if(snap) {at.X=(float)Math.Round(at.X/5)*5;at.Y=(float)Math.Round(at.Y/5)*5;}
            ghost.DiagramPoints[index]=Math.Max(13,Math.Min(246,at.X));
            ghost.DiagramPoints[index+1]=Math.Max(14,Math.Min(130,at.Y));
        }
        public static void DrawHandles(Graphics g,Mark mark,float zoom) {
            var p=Coordinates(mark);if(p==null)return;
            var view=new View(mark);
            for(int i=0;i<p.Length;i+=2) {
                var at=view.ToWorld(p[i],p[i+1]);float size=4.5f/zoom;
                Art.Dot(g,Art.White,at.X,at.Y,size);
                Art.Circle(g,Construction,at.X,at.Y,size,1.6f/zoom);
            }
        }
        static PointF Add(PointF p,PointF q) {return new PointF(p.X+q.X,p.Y+q.Y);}
        static PointF Sub(PointF p,PointF q) {return new PointF(p.X-q.X,p.Y-q.Y);}
        static PointF Mix(PointF p,PointF q,double t) {return new PointF((float)(p.X+(q.X-p.X)*t),(float)(p.Y+(q.Y-p.Y)*t));}
        static double Dot(PointF p,PointF q) {return (double)p.X*q.X+(double)p.Y*q.Y;}
        static double Cross(PointF p,PointF q) {return (double)p.X*q.Y-(double)p.Y*q.X;}
        static double Len(PointF p) {return Math.Sqrt(Dot(p,p));}
        static void Line(Graphics g,Pen p,PointF a,PointF b) {g.DrawLine(p,a,b);}
        public static void Draw(Graphics g,Mark mark) {
            var view=new View(mark);
            var state=g.Save();
            g.SetClip(mark.Bounds,CombineMode.Intersect);
            g.TranslateTransform(view.X,view.Y);g.RotateTransform(mark.Rotation);g.ScaleTransform(view.S,view.S);g.TranslateTransform(-130,-75);
            Color ink=Art.ColorOf(mark.Color,Art.C("#253345"));
            if(IsCircuit(mark)) Circuits.Draw(g,new RectangleF(4,5,252,140),mark,-1);
            else {
                var coordinates=Coordinates(mark);
                if(coordinates==null)Catalog.Sketch(g,mark.Text,new RectangleF(0,0,260,150),mark.Labels??true,ink);
                else DrawGeometry(g,mark,coordinates,ink);
            }
            g.Restore(state);
        }
        public static bool HasMeasurements(Mark m) {
            if(IsCircuit(m))return true;
            string s=m.Text;
            return s=="circle"||s=="tangent"||s=="sector"||s=="square"||s=="rectangle"||s=="rhombus"||s=="parallelogram"||
                s=="trapezoid"||s=="polygon"||s=="triangle"||s=="right-triangle"||s=="iso"||s=="equilateral"||s=="altitude"||
                s=="median"||s=="midline"||s=="bisector"||s=="inscribed"||s=="circumcircle";
        }
        static void DrawGeometry(Graphics g,Mark mark,float[] coordinates,Color ink) {
            var vertices=new PointF[coordinates.Length/2];
            for(int i=0;i<vertices.Length;i++)vertices[i]=new PointF(coordinates[2*i],coordinates[2*i+1]);
            string kind=mark.Text;
            using(var edge=Art.Pen(ink,1.85f)) using(var guide=Art.Pen(Construction,1.5f)) using(var hidden=Art.Pen(Art.C("#8995A1"),1.15f)) {
                hidden.DashStyle=DashStyle.Dash;
                var a=vertices[0];var b=vertices[1];
                switch(kind) {
                    case "circle":case "tangent":case "sector":
                        Circle(g,mark,vertices,edge,guide);return;
                    case "cube":case "box":case "section":
                        var c=vertices[2];var d=Sub(vertices[3],a);var corner=Add(b,Sub(c,a));
                        var front=new[]{a,b,corner,c};
                        var back=front.Select(p=>Add(p,d)).ToArray();
                        g.DrawPolygon(edge,front);g.DrawLines(edge,new[]{c,back[3],back[2],corner});
                        g.DrawLine(edge,b,back[1]);g.DrawLine(edge,back[1],back[2]);
                        g.DrawLines(hidden,new[]{a,back[0],back[3]});g.DrawLine(hidden,back[0],back[1]);
                        if(kind=="section") g.DrawPolygon(guide,new[]{Mix(a,c,.45),Mix(b,corner,.45),Mix(back[1],back[2],.45),Mix(back[0],back[3],.45)});
                        LabelPoints(g,mark,vertices);return;
                    case "prism":
                        var offset=Sub(vertices[3],a);
                        var lower=vertices.Take(3).ToArray();
                        var upper=lower.Select(p=>Add(p,offset)).ToArray();
                        g.DrawPolygon(edge,upper);g.DrawLines(edge,lower);g.DrawLine(hidden,lower[2],a);
                        for(int i=0;i<3;i++)g.DrawLine(edge,lower[i],upper[i]);
                        LabelPoints(g,mark,vertices);return;
                    case "pyramid":case "tetrahedron":
                        var basePoints=vertices.Take(vertices.Length-1).ToArray();var peak=vertices[vertices.Length-1];
                        g.DrawLines(edge,basePoints.Take(3).ToArray());
                        if(basePoints.Length==3) g.DrawLine(hidden,basePoints[0],basePoints[2]);
                        else g.DrawLines(hidden,new[]{basePoints[2],basePoints[3],basePoints[0]});
                        for(int i=0;i<basePoints.Length;i++)g.DrawLine(i==3?hidden:edge,peak,basePoints[i]);
                        LabelPoints(g,mark,vertices);return;
                    case "sphere":case "cone":case "cylinder":case "frustum":
                        RoundSolid(g,kind,vertices,edge,hidden);return;
                    case "angles":
                        Line(g,edge,a,b);Line(g,edge,vertices[2],vertices[3]);Line(g,guide,vertices[4],vertices[5]);return;
                    case "rays":
                        Line(g,edge,new PointF(26,a.Y),new PointF(234,a.Y));
                        Line(g,hidden,a,new PointF(a.X,12));
                        Arrow(g,guide,b,a);Arrow(g,guide,a,new PointF(2*a.X-b.X,b.Y));return;
                    case "pendulum":
                        Line(g,edge,new PointF(a.X-37,a.Y),new PointF(a.X+37,a.Y));
                        Line(g,hidden,a,new PointF(a.X,Math.Min(143,a.Y+(float)Ink.Distance(a,b))));
                        Line(g,edge,a,b);Art.Dot(g,Construction,b.X,b.Y,6);return;
                    case "projectile":
                        Line(g,edge,a,vertices[2]);var top=vertices[1];
                        var ctrl=new PointF(2*top.X-(a.X+vertices[2].X)/2,2*top.Y-(a.Y+vertices[2].Y)/2);
                        g.DrawBezier(guide,a,Mix(a,ctrl,2.0/3),Mix(vertices[2],ctrl,2.0/3),vertices[2]);
                        Arrow(g,edge,a,Mix(a,ctrl,.28));return;
                    case "lever":
                        Line(g,edge,a,b);var lever=Sub(b,a);
                        var pivot=Len(lever)<.1?a:Mix(a,b,Math.Max(0,Math.Min(1,Dot(Sub(vertices[2],a),lever)/Dot(lever,lever))));
                        g.DrawPolygon(guide,new[]{pivot,new PointF(pivot.X-14,pivot.Y+28),new PointF(pivot.X+14,pivot.Y+28)});
                        Arrow(g,guide,new PointF(a.X,a.Y-25),a);Arrow(g,guide,new PointF(b.X,b.Y-25),b);return;
                    case "forces":
                        var axis=Sub(b,a);var length=Len(axis);
                        if(length<2)return;
                        g.DrawPolygon(edge,new[]{a,b,new PointF(b.X,a.Y)});
                        var u=new PointF((float)(axis.X/length),(float)(axis.Y/length));
                        var n=new PointF(u.Y,-u.X);
                        var foot=Mix(a,b,Math.Max(.07,Math.Min(.93,Dot(Sub(vertices[2],a),axis)/Dot(axis,axis))));
                        var left=Add(foot,new PointF(-10*u.X,-10*u.Y));var right=Add(foot,new PointF(10*u.X,10*u.Y));
                        var topShift=new PointF(19*n.X,19*n.Y);
                        g.DrawPolygon(edge,new[]{left,right,Add(right,topShift),Add(left,topShift)});
                        var middle=Mix(foot,Add(foot,topShift),.5);
                        Arrow(g,guide,middle,new PointF(middle.X,middle.Y+38));
                        Arrow(g,guide,middle,Add(middle,new PointF(40*n.X,40*n.Y)));return;
                }
                if(vertices.Length==3) Triangle(g,mark,vertices,guide,hidden);
                var fill=Art.ColorOf(mark.Fill,Color.Transparent);
                if(fill.A>0)using(var brush=new SolidBrush(Color.FromArgb(75,fill)))g.FillPolygon(brush,vertices);
                g.DrawPolygon(edge,vertices);
                LabelPoints(g,mark,vertices);
                if(mark.Measurements) {
                    var store=Metrics(vertices);
                    if(store.Length>0) Art.Label(g,store,6,133,248,16,9,Construction,true,StringAlignment.Center);
                }
            }
        }
        static void RoundSolid(Graphics g,string kind,PointF[] v,Pen edge,Pen hidden) {
            var top=v[0];var bottom=v[1];
            if(kind=="sphere") {
                float radius=(float)Ink.Distance(top,bottom);
                if(radius<.5f)return;
                g.DrawEllipse(edge,top.X-radius,top.Y-radius,2*radius,2*radius);
                g.DrawArc(edge,top.X-radius,top.Y-radius*.3f,2*radius,radius*.6f,0,180);
                g.DrawArc(hidden,top.X-radius,top.Y-radius*.3f,2*radius,radius*.6f,180,180);
                return;
            }
            float lower=(float)Ink.Distance(kind=="cylinder"?top:bottom,v[2]);
            float upper=kind=="frustum"?(float)Ink.Distance(top,v[3]):lower;
            if(lower<.5f||upper<.5f)return;
            var rim=new RectangleF(bottom.X-lower,bottom.Y-lower*.19f,lower*2,lower*.38f);
            g.DrawArc(edge,rim,0,180);g.DrawArc(hidden,rim,180,180);
            if(kind=="cone") upper=0;
            else g.DrawEllipse(edge,top.X-upper,top.Y-upper*.19f,upper*2,upper*.38f);
            g.DrawLine(edge,top.X-upper,top.Y,bottom.X-lower,bottom.Y);
            g.DrawLine(edge,top.X+upper,top.Y,bottom.X+lower,bottom.Y);
        }
        static void Circle(Graphics g,Mark mark,PointF[] points,Pen edge,Pen guide) {
            var a=points[0];var b=points[1];float radius=(float)Ink.Distance(a,b);
            if(radius<.25f)return;
            g.DrawEllipse(edge,a.X-radius,a.Y-radius,radius*2,radius*2);
            Line(g,guide,a,b);
            if(mark.Text=="tangent") {
                var v=Sub(b,a);
                Line(g,guide,new PointF(b.X-v.Y/radius*60,b.Y+v.X/radius*60),new PointF(b.X+v.Y/radius*60,b.Y-v.X/radius*60));
            }
            if(mark.Text=="sector") {
                var c=points[2];double len=Ink.Distance(a,c);
                if(len>.25) {c=Mix(a,c,radius/len);Line(g,guide,a,c);Line(g,guide,b,c);}
            }
            if(mark.Measurements)Art.Label(g,"r = "+radius.ToString("0.0",CultureInfo.CurrentCulture)+" усл. ед.",0,133,260,16,9,Construction,true,StringAlignment.Center);
            LabelPoints(g,mark,points);
        }
        static void Triangle(Graphics g,Mark m,PointF[] v,Pen guide,Pen hidden) {
            var a=v[0];var b=v[1];var c=v[2];
            double ab=Ink.Distance(a,b),ac=Ink.Distance(a,c),bc=Ink.Distance(b,c);
            if(Math.Min(ab,Math.Min(ac,bc))<.25)return;
            switch(m.Text) {
                case "altitude":
                    var axis=Sub(c,b);double t=Dot(Sub(a,b),axis)/Dot(axis,axis);
                    var foot=Mix(b,c,t);
                    if(t<0||t>1)Line(g,hidden,foot,t<0?b:c);
                    Line(g,guide,a,foot);
                    double alt=Ink.Distance(a,foot),baseLen=Len(axis);
                    if(alt>10&&baseLen>10) {
                        var side=new PointF((float)(7*axis.X/baseLen),(float)(7*axis.Y/baseLen));
                        var up=Sub(Mix(foot,a,7/alt),foot);
                        g.DrawLines(guide,new[]{Add(foot,side),Add(Add(foot,side),up),Add(foot,up)});
                    }
                    break;
                case "median":Line(g,guide,a,Mix(b,c,.5));break;
                case "midline":Line(g,guide,Mix(a,b,.5),Mix(a,c,.5));break;
                case "bisector":Line(g,guide,a,Mix(b,c,ab/(ab+ac)));break;
                case "inscribed":
                    double s=ab+ac+bc;
                    var i=new PointF((float)((a.X*bc+b.X*ac+c.X*ab)/s),(float)((a.Y*bc+b.Y*ac+c.Y*ab)/s));
                    float r=(float)(Math.Abs(Cross(Sub(b,a),Sub(c,a)))/s);
                    if(r>.25f)g.DrawEllipse(guide,i.X-r,i.Y-r,2*r,2*r);
                    break;
                case "circumcircle":
                    var u=Sub(b,a);var w=Sub(c,a);double den=2*Cross(u,w);
                    if(Math.Abs(den)>.1) {
                        var center=Add(a,new PointF((float)((w.Y*Dot(u,u)-u.Y*Dot(w,w))/den),(float)((u.X*Dot(w,w)-w.X*Dot(u,u))/den)));
                        var rr=(float)Ink.Distance(center,a);
                        if(rr<5000)g.DrawEllipse(guide,center.X-rr,center.Y-rr,rr*2,rr*2);
                    }
                    break;
            }
        }
        static void LabelPoints(Graphics g,Mark mark,PointF[] points) {
            if(!(mark.Labels??true))return;
            for(int i=0;i<points.Length;i++) {
                var p=points[i];
                Art.Label(g,((char)('A'+i)).ToString(),p.X>227?p.X-15:p.X+5,Math.Max(1,p.Y-17),15,13,10,Art.Muted,true);
            }
        }
        internal static string Metrics(PointF[] p) {
            double twiceArea=0,length=0;
            for(int i=0;i<p.Length;i++) {var next=p[(i+1)%p.Length];twiceArea+=Cross(p[i],next);length+=Ink.Distance(p[i],next);}
            if(p.Length==3&&Math.Abs(twiceArea)>.1) {
                var angles=new string[3];
                for(int i=0;i<3;i++) {
                    var a=Sub(p[(i+1)%3],p[i]);var b=Sub(p[(i+2)%3],p[i]);
                    angles[i]=Math.Round(Math.Atan2(Math.Abs(Cross(a,b)),Dot(a,b))*180/Math.PI,1).ToString("0.#")+"°";
                }
                return "∠A "+angles[0]+"    ∠B "+angles[1]+"    ∠C "+angles[2];
            }
            return Math.Abs(twiceArea)>.1?"P = "+length.ToString("0.0")+" ед.   ·   S = "+(Math.Abs(twiceArea)/2).ToString("0.0")+" ед.²":"";
        }
        static void Arrow(Graphics g,Pen pen,PointF a,PointF b) {
            g.DrawLine(pen,a,b);var t=Math.Atan2(b.Y-a.Y,b.X-a.X);
            g.DrawLines(pen,new[]{new PointF(b.X-(float)(7*Math.Cos(t-.52)),b.Y-(float)(7*Math.Sin(t-.52))),b,
                new PointF(b.X-(float)(7*Math.Cos(t+.52)),b.Y-(float)(7*Math.Sin(t+.52)))});
        }
        public static void Properties(Workspace owner,Mark mark,Action<Mark> done) {
            if(IsCircuit(mark)){Circuits.Edit(owner,mark,done);return;}
            var copy=mark.Copy();
            var sheet=new Sheet("Чертёж",HasPoints(mark)?"Вершины можно перетаскивать прямо на доске":"Поворот и размер рисунка",508,435);
            var pane=sheet.Put(new DiagramPane{BackColor=Art.Paper},22,98,464,180);
            pane.Render=(g,bounds)=>{var sample=copy.Copy();sample.X=6;sample.Y=6;sample.W=bounds.Width-12;sample.H=bounds.Height-12;Draw(g,sample);};
            var rotation=sheet.Field(mark.Rotation.ToString("0.#"),22,312,106);
            var scale=sheet.Field("100",148,312,106);
            sheet.Caption("Поворот, °",22,287,106);sheet.Caption("Размер, %",148,287,106);
            if(HasPoints(mark)) sheet.Action("Исходные точки",305,310,180,()=>{copy.DiagramPoints=null;pane.Invalidate();});
            double angle=mark.Rotation,percent=100;
            rotation.TextChanged+=(s,e)=>{if(Parse(rotation.Text,out angle)&&Math.Abs(angle)<=360) {copy.Rotation=(float)angle;pane.Invalidate();}};
            sheet.Footer("Применить",()=>{
                if(!Parse(rotation.Text,out angle)||Math.Abs(angle)>360||!Parse(scale.Text,out percent)||percent<20||percent>500)
                    return sheet.Error("Поворот до 360°, размер от 20 до 500 %.");
                copy.Rotation=(float)angle;
                copy.W=mark.W*(float)percent/100;copy.H=mark.H*(float)percent/100;
                copy.X=mark.X+(mark.W-copy.W)/2;copy.Y=mark.Y+(mark.H-copy.H)/2;
                return true;
            });
            sheet.OnBoard(owner,result=>done(result==DialogResult.OK?copy:null));
        }
        public static bool Parse(string text,out double value) {
            return Double.TryParse(text.Trim().Replace(',','.').Replace('−','-'),NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&
                !Double.IsNaN(value)&&!Double.IsInfinity(value);
        }
    }
}
