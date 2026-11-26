using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace Polya {
    static class Art {
        static readonly Dictionary<string,Color> colors=new Dictionary<string,Color>();
        public static readonly Color Ink=C("#1F2025"), Muted=C("#5F606A"), Fade=C("#8B8A97"), Line=C("#E7E7ED"),
            Purple=C("#6965DB"), Pale=C("#E5E3FE"), White=Color.White, Paper=C("#F8F8FA"), Sidebar=C("#F1F0F5"), Dark=C("#242336");
        static readonly Dictionary<string,Font> fonts=new Dictionary<string,Font>();
        public static Color C(string hex) {
            Color color;
            if(!colors.TryGetValue(hex,out color))colors[hex]=color=ColorTranslator.FromHtml(hex);
            return color;
        }
        public static Color ColorOf(string hex,Color fallback) {
            if(String.IsNullOrEmpty(hex)||hex.Length!=7||hex[0]!='#') return fallback;
            int rgb;
            return Int32.TryParse(hex.Substring(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out rgb)?Color.FromArgb(unchecked((int)0xff000000)|rgb):fallback;
        }
        public static Font Font(float px,bool bold=false,bool mono=false,string sample=null) {
            bool cyrillic=BoardFonts.IsCyrillic(sample);
            string key=(mono?"m":cyrillic?"c":"l")+(bold?"b":"")+px.ToString(CultureInfo.InvariantCulture);
            Font f;
            if(!fonts.TryGetValue(key,out f)) fonts[key]=f=mono?new Font("Consolas",px,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel):
                BoardFonts.Create(px,bold,cyrillic);
            return f;
        }
        public static Font NoteFont(float px) {
            string key="n"+px.ToString(CultureInfo.InvariantCulture);
            Font f;
            if(!fonts.TryGetValue(key,out f)) fonts[key]=f=new Font("Segoe UI",px,FontStyle.Regular,GraphicsUnit.Pixel);
            return f;
        }
        public static void Prepare(Graphics g) {
            g.SmoothingMode=SmoothingMode.AntiAlias; g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit; g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        }
        public static void Fill(Graphics g,Color c,RectangleF r) { using(var b=new SolidBrush(c)) g.FillRectangle(b,r); }
        public static void Dot(Graphics g,Color c,float x,float y,float radius) { using(var b=new SolidBrush(c)) g.FillEllipse(b,x-radius,y-radius,radius*2,radius*2); }
        public static void Circle(Graphics g,Color c,float x,float y,float radius,float weight) { using(var p=Pen(c,weight)) g.DrawEllipse(p,x-radius,y-radius,radius*2,radius*2); }
        internal static GraphicsPath Path(RectangleF r,float radius) {
            var p=new GraphicsPath();
            float s=Math.Min(radius*2,Math.Min(r.Width,r.Height));
            if(s<=0) { if(r.Width>0&&r.Height>0) p.AddRectangle(r); return p; }
            p.AddArc(r.Left,r.Top,s,s,180,90); p.AddArc(r.Right-s,r.Top,s,s,270,90);
            p.AddArc(r.Right-s,r.Bottom-s,s,s,0,90); p.AddArc(r.Left,r.Bottom-s,s,s,90,90); p.CloseFigure();
            return p;
        }
        public static void Round(Graphics g,RectangleF r,float radius,Color color,Color? border=null) {
            using(var p=Path(r,radius)) using(var b=new SolidBrush(color)) {
                g.FillPath(b,p);
                if(border.HasValue) using(var stroke=Pen(border.Value,1)) g.DrawPath(stroke,p);
            }
        }
        public static Pen Pen(Color color,float weight) { return new Pen(color,weight) { StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round }; }
        public static void Stroke(Graphics g,Color c,float weight,params float[] coords) {
            var pts=new PointF[coords.Length/2];
            for(int i=0;i<pts.Length;i++) pts[i]=new PointF(coords[i*2],coords[i*2+1]);
            using(var p=Pen(c,weight)) g.DrawLines(p,pts);
        }
        public static void Label(Graphics g,string text,float x,float y,float w,float h,float size,Color c,bool bold=false,StringAlignment align=StringAlignment.Near,bool mono=false,Font face=null) {
            if(String.IsNullOrEmpty(text)||w<=0||h<=0) return;
            using(var b=new SolidBrush(c))
            using(var f=new StringFormat { Alignment=align, LineAlignment=StringAlignment.Center, Trimming=StringTrimming.EllipsisCharacter, FormatFlags=StringFormatFlags.NoWrap }) {
                g.DrawString(text,face??Font(size,bold,mono,text),b,new RectangleF(x,y,w,h),f);
            }
        }
        public static void Paragraph(Graphics g,string text,float x,float y,float w,float h,float size,Color color) {
            if(String.IsNullOrEmpty(text)||w<=0||h<=0) return;
            using(var b=new SolidBrush(color)) using(var f=new StringFormat { FormatFlags=StringFormatFlags.LineLimit }) g.DrawString(text,Font(size,sample:text),b,new RectangleF(x,y,w,h),f);
        }
        public static void Icon(Graphics g,string name,float x,float y,float size,Color c) {
            var state=g.Save(); g.TranslateTransform(x,y); g.ScaleTransform(size/24,size/24);
            switch(name) {
                case "logo":
                    Stroke(g,c,1.9f,5,19,5,7,18,7,18,19,5,19); Stroke(g,c,1.9f,9,7,9,4,21,4,21,15,18,15);
                    Stroke(g,c,1.6f,8,15,10,12,13,14,15,10); break;
                case "select":Stroke(g,c,1.7f,6,4,18,13,12,14,10,20,6,4); break;
                case "hand":Stroke(g,c,1.6f,6,12,6,7,8,6,9.5f,7,9.5f,12,9.5f,4,12,3.5f,13,5,13,12,13,6,15,5.5f,16,7,16,12,16,8,18,7.5f,19,9,19,15,17,20,10,20,5,14,4,12,6,11,9.5f,14);break;
                case "pen":Stroke(g,c,1.7f,4,19,5,14,15,4,17,4,20,7,20,9,10,19,4,20,4,19);
                    Stroke(g,c,1.6f,13,6,18,11);break;
                case "rect":Round(g,new RectangleF(5,5,14,14),2,Color.Transparent,c);break;
                case "ellipse":Circle(g,c,12,12,7,1.6f);break;
                case "diamond":Stroke(g,c,1.6f,12,4,20,12,12,20,4,12,12,4);break;
                case "more":Dot(g,c,12,5.5f,1.4f);Dot(g,c,12,12,1.4f);Dot(g,c,12,18.5f,1.4f);break;
                case "lock":case "unlock":
                    Round(g,new RectangleF(6,10,12,10),2,Color.Transparent,c);
                    if(name=="lock") Stroke(g,c,1.5f,9,10,9,7,10,5,14,5,15,7,15,10);
                    else Stroke(g,c,1.5f,9,10,9,7,10,5,14,5,16,7);
                    Dot(g,c,12,15,1.1f);break;
                case "line":Stroke(g,c,1.7f,5,19,19,5);break;
                case "arrow":Stroke(g,c,1.7f,5,19,19,5,12,5);Stroke(g,c,1.7f,19,5,19,12);break;
                case "text":Stroke(g,c,1.7f,5,7,5,5,19,5,19,7);Stroke(g,c,1.7f,12,5,12,19);Stroke(g,c,1.7f,9,19,15,19);break;
                case "note":Stroke(g,c,1.6f,14,20,5,20,5,4,19,4,19,15,14,20,14,15,19,15);break;
                case "laser":Stroke(g,c,1.7f,6,18,12,9,16,12,10,21,6,18);Stroke(g,c,1.4f,14,6,16,3);Stroke(g,c,1.4f,18,9,21,8);break;
                case "pixel": Stroke(g,c,1.6f,3,15,12,6,18,12,9,21,3,15);Dot(g,c,17,18,1);Dot(g,c,21,15,1);Dot(g,c,21,20,1);break;
                case "link":Stroke(g,c,1.7f,10,7,13,4,17,4,20,7,20,10,16,14,13,14);Stroke(g,c,1.7f,14,17,11,20,7,20,4,17,4,14,8,10,11,10);Stroke(g,c,1.7f,9,15,15,9);break;
                case "eraser":Stroke(g,c,1.6f,9,19,4,14,4,12,12,4,14,4,20,10,20,12,13,19,9,19,20,19);Stroke(g,c,1.6f,8,9,15,16);break;
                case "undo":case "redo":
                    if(name=="redo"){g.TranslateTransform(24,0);g.ScaleTransform(-1,1);}
                    Stroke(g,c,1.7f,9,5,4,10,9,15);Stroke(g,c,1.7f,4,10,14,10,17,11,19,14,19,17);break;
                case "math":Stroke(g,c,1.8f,18,5,7,5,13,12,7,19,18,19);break;
                case "physics":
                    using(var p=Pen(c,1.4f)) { g.DrawEllipse(p,3,8,18,8);g.RotateTransform(60,MatrixOrder.Prepend); }
                    g.Restore(state); state=g.Save(); g.TranslateTransform(x+size/2,y+size/2); g.ScaleTransform(size/24,size/24);
                    using(var p=Pen(c,1.4f)) { g.RotateTransform(60);g.DrawEllipse(p,-9,-4,18,8);g.RotateTransform(60);g.DrawEllipse(p,-9,-4,18,8); }
                    Dot(g,c,0,0,1.6f);break;
                case "russian":Label(g,"Я",0,0,24,23,18,c,true,StringAlignment.Center);break;
                case "other":Round(g,new RectangleF(5,5,14,14),3,Color.Transparent,c);Stroke(g,c,1.4f,5,10,19,10);break;
                case "graph":Stroke(g,c,1.5f,4,4,4,20,20,20);Stroke(g,c,1.8f,6,15,9,10,12,13,16,6,20,7);break;
                case "trig":Circle(g,c,12,12,7,1.4f);Stroke(g,c,1.3f,3,12,21,12);Stroke(g,c,1.3f,12,3,12,21);Stroke(g,c,1.7f,12,12,17,7);break;
                case "calc":Round(g,new RectangleF(6,3,12,18),2,Color.Transparent,c);Stroke(g,c,1.5f,8,8,16,8);
                    for(int row=0;row<2;row++) for(int col=0;col<2;col++) Dot(g,c,10+col*4,12+row*4,1);break;
                case "book":Stroke(g,c,1.55f,12,7,8,5,4,5,4,18,8,18,12,20,16,18,20,18,20,5,16,5,12,7,12,20);break;
                case "plus":Stroke(g,c,1.6f,5,12,19,12);Stroke(g,c,1.6f,12,5,12,19);break;
                case "minus":Stroke(g,c,1.6f,5,12,19,12);break;
                case "chevron":Stroke(g,c,1.7f,9,6,15,12,9,18);break;
                case "down":Stroke(g,c,1.7f,6,9,12,15,18,9);break;
                case "up":Stroke(g,c,1.7f,6,15,12,9,18,15);break;
                case "menu":Stroke(g,c,1.65f,5,7,19,7);Stroke(g,c,1.65f,5,12,19,12);Stroke(g,c,1.65f,5,17,19,17);break;
                case "back":Stroke(g,c,1.7f,15,6,9,12,15,18);break;
                case "frame":
                    Round(g,new RectangleF(4,5,16,14),2,Color.Transparent,c);
                    Stroke(g,c,1.45f,4,10,20,10);break;
                case "image":
                    Round(g,new RectangleF(4,5,16,14),2,Color.Transparent,c);
                    Dot(g,c,9,9,1.2f);Stroke(g,c,1.4f,6,16,11,12,14,15,17,11,20,15);break;
                case "task":
                    Round(g,new RectangleF(5,4,14,16),2,Color.Transparent,c);
                    Stroke(g,c,1.5f,8,12,11,15,16,9);break;
                case "timer":
                    Circle(g,c,12,13,7,1.6f);Stroke(g,c,1.6f,12,9,12,13,15,15);Stroke(g,c,1.6f,9,3,15,3);break;
                case "play":Stroke(g,c,1.7f,8,5,18,12,8,19,8,5);break;
                case "pause":Stroke(g,c,1.9f,9,5,9,19);Stroke(g,c,1.9f,15,5,15,19);break;
                case "settings":
                    Stroke(g,c,1.6f,4,6,20,6);Stroke(g,c,1.6f,4,12,20,12);Stroke(g,c,1.6f,4,18,20,18);
                    Dot(g,c,9,6,2.4f);Dot(g,c,15,12,2.4f);Dot(g,c,8,18,2.4f);break;
                case "background":
                    Round(g,new RectangleF(4,4,16,16),2,Color.Transparent,c);
                    for(int i=0;i<3;i++) for(int j=0;j<3;j++) Dot(g,c,8+i*4,8+j*4,.8f);break;
                case "geometry":Stroke(g,c,1.6f,4,19,12,5,20,19,4,19);Circle(g,c,12,14.4f,3.7f,1.2f);break;
                case "scheme":
                    Stroke(g,c,1.6f,8,5,5,5,5,19,8,19);Stroke(g,c,1.6f,16,5,19,5,19,19,16,19);
                    Stroke(g,c,1.6f,8,12,11,12);Stroke(g,c,1.6f,13,12,16,12);break;
                case "open":Stroke(g,c,1.6f,3,18,3,6,9,6,11,8,20,8,20,10);Stroke(g,c,1.6f,3,18,6,10,22,10,19,18,3,18);break;
                case "save":Stroke(g,c,1.6f,5,4,16,4,20,8,20,20,4,20,4,4,5,4);Stroke(g,c,1.6f,8,4,8,9,15,9,15,4);Stroke(g,c,1.6f,8,20,8,14,16,14,16,20);break;
                case "check":Stroke(g,c,1.8f,5,12,10,17,19,7);break;
                case "spark":Stroke(g,c,1.5f,12,3,14,10,21,12,14,14,12,21,10,14,3,12,10,10,12,3);break;
                case "external":Stroke(g,c,1.6f,12,5,19,5,19,12);Stroke(g,c,1.6f,19,5,10,14);Stroke(g,c,1.6f,9,6,5,6,5,19,18,19,18,15);break;
                case "fit":Stroke(g,c,1.6f,5,9,5,5,9,5);Stroke(g,c,1.6f,15,5,19,5,19,9);Stroke(g,c,1.6f,19,15,19,19,15,19);Stroke(g,c,1.6f,9,19,5,19,5,15);break;
                case "edit":Stroke(g,c,1.6f,5,19,6,14,16,4,20,8,10,18,5,19);Stroke(g,c,1.6f,14,6,18,10);break;
                case "help":Circle(g,c,12,12,8,1.5f);Stroke(g,c,1.5f,9,9,10,7.5f,13,7.5f,15,9,15,11,12,13,12,14);Dot(g,c,12,17,1);break;
                case "close":Stroke(g,c,1.6f,6,6,18,18);Stroke(g,c,1.6f,18,6,6,18);break;
                case "trash":Stroke(g,c,1.6f,5,7,19,7);Stroke(g,c,1.6f,8,7,8,4,16,4,16,7);Stroke(g,c,1.6f,6,7,7,20,17,20,18,7);break;
            }
            g.Restore(state);
        }
    }
}
