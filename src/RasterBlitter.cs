using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Polya {
    sealed class RasterBlitter : IDisposable {
        [StructLayout(LayoutKind.Sequential)]
        struct Header {
            public uint Size;public int Width,Height;
            public ushort Planes,Bits;
            public uint Compression,ImageSize;
            public int XDensity,YDensity;
            public uint Colors,ImportantColors;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct Blend {
            public byte Op,Flags,Constant,Format;
        }
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr bitmap);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr bitmap);
        [DllImport("gdi32.dll")] static extern int SaveDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool RestoreDC(IntPtr dc,int saved);
        [DllImport("gdi32.dll")] static extern int IntersectClipRect(IntPtr dc,int x1,int y1,int x2,int y2);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst,int x,int y,int w,int h,IntPtr src,int sx,int sy,uint mode);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc,ref Header header,uint mode,out IntPtr location,IntPtr section,uint offset);
        [DllImport("msimg32.dll")] static extern bool AlphaBlend(IntPtr dst,int x,int y,int w,int h,IntPtr src,int sx,int sy,int sw,int sh,Blend blend);

        internal sealed class Image : IDisposable {
            public IntPtr Handle {get;private set;}
            public readonly Bitmap Bitmap;
            readonly IntPtr pixels;
            public Image(int width,int height) {
                IntPtr location;
                var header=new Header{Size=40,Width=width,Height=-height,Planes=1,Bits=32};
                Handle=CreateDIBSection(IntPtr.Zero,ref header,0,out location,IntPtr.Zero,0);
                pixels=location;
                try {Bitmap=Handle==IntPtr.Zero?new Bitmap(width,height,PixelFormat.Format32bppPArgb):
                    new Bitmap(width,height,width*4,PixelFormat.Format32bppPArgb,location);}
                catch {if(Handle!=IntPtr.Zero)DeleteObject(Handle);throw;}
                using(var g=Graphics.FromImage(Bitmap))g.Clear(Color.Transparent);
            }
            public void Dispose() {Bitmap.Dispose();if(Handle!=IntPtr.Zero){DeleteObject(Handle);Handle=IntPtr.Zero;}}
            internal void WriteRows(int[][] colors,int[] rows,int width,int height) {
                if(pixels!=IntPtr.Zero) {
                    for(int y=0;y<height;y++)
                        Marshal.Copy(colors[rows[y]],0,IntPtr.Add(pixels,y*Bitmap.Width*4),width);
                    return;
                }
                var region=new Rectangle(0,0,width,height);
                var data=Bitmap.LockBits(region,ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
                try {for(int y=0;y<height;y++)Marshal.Copy(colors[rows[y]],0,IntPtr.Add(data.Scan0,y*data.Stride),width);}
                finally {Bitmap.UnlockBits(data);}
            }
        }
        internal struct Patch {
            public Image Native;
            public Bitmap Bitmap;
            public RectangleF Destination,Clip,Source;
            public bool Opaque;
        }
        readonly IntPtr memory=CreateCompatibleDC(IntPtr.Zero);
        public void Dispose() {if(memory!=IntPtr.Zero)DeleteDC(memory);}
        public void Draw(Graphics g,List<Patch> patches,RectangleF view,RectangleF source) {
            if(patches.Count==0)return;
            var points=new[]{PointF.Empty,new PointF(1,1)};
            g.TransformPoints(CoordinateSpace.Device,CoordinateSpace.World,points);
            float ox=points[0].X,oy=points[0].Y,sx=points[1].X-ox,sy=points[1].Y-oy;
            Func<RectangleF,Rectangle> device=r=>Rectangle.FromLTRB((int)Math.Round(ox+r.Left*sx),(int)Math.Round(oy+r.Top*sy),
                (int)Math.Round(ox+r.Right*sx),(int)Math.Round(oy+r.Bottom*sy));
            var native=IntPtr.Zero;
            if(memory!=IntPtr.Zero)try {native=g.GetHdc();}catch(ExternalException) {}
            var failed=new List<Patch>();
            try {
                int saved=native==IntPtr.Zero?0:SaveDC(native);
                if(saved!=0) {
                    var viewport=device(view);
                    IntersectClipRect(native,viewport.Left,viewport.Top,viewport.Right,viewport.Bottom);
                }
                var blend=new Blend{Constant=255,Format=1};
                foreach(var patch in patches) {
                    if(saved==0||patch.Native==null||patch.Native.Handle==IntPtr.Zero) {failed.Add(patch);continue;}
                    var dest=device(patch.Destination);
                    if(dest.Width<=0||dest.Height<=0)continue;
                    int part=0;
                    if(!patch.Clip.IsEmpty) {
                        part=SaveDC(native);var clip=device(patch.Clip);
                        if(part!=0)IntersectClipRect(native,clip.Left,clip.Top,clip.Right,clip.Bottom);
                    }
                    if(!patch.Clip.IsEmpty&&part==0){failed.Add(patch);continue;}
                    var prior=SelectObject(memory,patch.Native.Handle);
                    if(prior!=IntPtr.Zero&&prior!=new IntPtr(-1)) {
                        try {
                            var crop=patch.Source.IsEmpty?source:patch.Source;
                            bool drawn=patch.Opaque&&dest.Width==(int)crop.Width&&dest.Height==(int)crop.Height?
                                BitBlt(native,dest.X,dest.Y,dest.Width,dest.Height,memory,(int)crop.X,(int)crop.Y,0x00CC0020):
                                AlphaBlend(native,dest.X,dest.Y,dest.Width,dest.Height,memory,(int)crop.X,(int)crop.Y,
                                    (int)crop.Width,(int)crop.Height,blend);
                            if(!drawn)failed.Add(patch);
                        } finally {SelectObject(memory,prior);}
                    } else failed.Add(patch);
                    if(part!=0)RestoreDC(native,part);
                }
                if(saved!=0)RestoreDC(native,saved);
            } finally {if(native!=IntPtr.Zero)g.ReleaseHdc(native);}
            if(failed.Count==0)return;
            var state=g.Save();g.SetClip(view,CombineMode.Intersect);
            g.InterpolationMode=InterpolationMode.Bilinear;g.CompositingQuality=CompositingQuality.HighSpeed;
            foreach(var patch in failed) {
                var crop=patch.Source.IsEmpty?source:patch.Source;
                if(!patch.Clip.IsEmpty) {
                    var clipped=g.Save();g.SetClip(patch.Clip,CombineMode.Intersect);
                    g.DrawImage(patch.Bitmap,patch.Destination,crop,GraphicsUnit.Pixel);g.Restore(clipped);
                } else g.DrawImage(patch.Bitmap,patch.Destination,crop,GraphicsUnit.Pixel);
            }
            g.Restore(state);
        }
    }
}
