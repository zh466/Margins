using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Polya {
    sealed class BoardGrid : IDisposable {
        readonly RasterBlitter compositor=new RasterBlitter();
        const int Shades=48;
        readonly List<RasterBlitter.Patch> patches=new List<RasterBlitter.Patch>(1);
        readonly int[][] colors=new int[Shades][];
        readonly bool[] valid=new bool[Shades];
        readonly int[] palette=new int[Shades*Shades];
        int[] columns=new int[0],rows=new int[0];
        Color palettePaper;
        RasterBlitter.Image image;

        // Line centers retain their fractional position at every zoom. Pixel coverage
        // is shared by adjacent pixels instead of rounding the grid's repeating period.
        internal static void FillAxis(int[] pixels,int size,double origin,double spacing,float blend) {
            Array.Clear(pixels,0,size);
            if(blend<1)AddLevel(pixels,size,origin,spacing*2,1-blend);
            if(blend>0)AddLevel(pixels,size,origin,spacing,blend);
        }
        static void AddLevel(int[] pixels,int size,double origin,double spacing,float blend) {
            long line=(long)Math.Ceiling((-1-origin)/spacing);
            for(double center=origin+line*spacing;center<=size;center+=spacing,line++) {
                bool major=line%5==0;
                double alpha=(major?46:23)*blend;
                // Integer coordinates represent pixel boundaries, not centers.
                double coordinate=center-.5;
                int left=(int)Math.Floor(coordinate);
                double fraction=coordinate-left;
                if(left>=0&&left<size)pixels[left]+=(int)Math.Round(alpha*(1-fraction));
                if(left+1>=0&&left+1<size)pixels[left+1]+=(int)Math.Round(alpha*fraction);
            }
        }
        public void Draw(Graphics g,RectangleF clip,Color paper,float spacing,float x,float y) {
            var device=new[]{clip.Location,new PointF(clip.Right,clip.Bottom),new PointF(x,y),new PointF(x+spacing,y+spacing)};
            g.TransformPoints(CoordinateSpace.Device,CoordinateSpace.World,device);
            int left=(int)Math.Round(device[0].X),top=(int)Math.Round(device[0].Y);
            int width=(int)Math.Round(device[1].X)-left,height=(int)Math.Round(device[1].Y)-top;
            if(width<=0||height<=0)return;
            float blend=Math.Max(0,Math.Min(1,(spacing-8)/3));
            if(image==null||width>columns.Length||height>rows.Length)Grow(width,height);
            FillAxis(columns,width,device[2].X-left,device[3].X-device[2].X,blend);
            FillAxis(rows,height,device[2].Y-top,device[3].Y-device[2].Y,blend);
            UpdatePalette(paper);
            Array.Clear(valid,0,valid.Length);
            for(int yIndex=0;yIndex<height;yIndex++) {
                int shade=rows[yIndex];
                if(valid[shade])continue;
                var row=colors[shade];
                int offset=shade*Shades;
                for(int xIndex=0;xIndex<width;xIndex++)row[xIndex]=palette[offset+columns[xIndex]];
                valid[shade]=true;
            }
            image.WriteRows(colors,rows,width,height);
            patches.Clear();
            patches.Add(new RasterBlitter.Patch {Native=image,Bitmap=image.Bitmap,Destination=clip,
                Source=new RectangleF(0,0,width,height),Opaque=true});
            compositor.Draw(g,patches,clip,RectangleF.Empty);
            patches.Clear();
        }
        void Grow(int w,int h) {
            if(image!=null)image.Dispose();
            int width=Math.Max(columns.Length,((w+127)/128)*128),height=Math.Max(rows.Length,((h+127)/128)*128);
            columns=new int[width];rows=new int[height];
            for(int i=0;i<colors.Length;i++)colors[i]=new int[width];
            image=new RasterBlitter.Image(width,height);
        }
        void UpdatePalette(Color paper) {
            if(paper==palettePaper)return;
            palettePaper=paper;
            for(int a=0;a<Shades;a++)for(int b=0;b<Shades;b++) {
                int alpha=a+b-(a*b+127)/255;
                int r=(paper.R*(255-alpha)+92*alpha+127)/255;
                int g=(paper.G*(255-alpha)+92*alpha+127)/255;
                int blue=(paper.B*(255-alpha)+92*alpha+127)/255;
                palette[a*Shades+b]=unchecked((int)0xff000000)|(r<<16)|(g<<8)|blue;
            }
        }
        public void Dispose() {
            if(image!=null){image.Dispose();image=null;}
            compositor.Dispose();
        }
    }
}
