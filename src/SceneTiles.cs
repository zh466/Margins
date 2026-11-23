using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace Polya {
    // Transparent raster tiles cache the committed scene. Gestures and grid stay vector.
    sealed class SceneTiles : IDisposable {
        const int Pixels=384, Gutter=3, Capacity=144;
        readonly Dictionary<Key,Tile> tiles=new Dictionary<Key,Tile>();
        readonly List<Tile> work=new List<Tile>();
        readonly RasterBlitter compositor=new RasterBlitter();
        SpatialIndex index;
        Action<Graphics,Mark,RectangleF> paint;
        long tick;
        int level,previous;
        bool initialized,requestChanged;
        RectangleF viewport;
        static readonly RectangleF Source=new RectangleF(Gutter,Gutter,Pixels,Pixels);
        struct Key : IEquatable<Key> {
            public readonly int Level,X,Y;
            public Key(int level,int x,int y) {Level=level;X=x;Y=y;}
            public bool Equals(Key other) {return Level==other.Level&&X==other.X&&Y==other.Y;}
            public override bool Equals(object value) {return value is Key&&Equals((Key)value);}
            public override int GetHashCode() {unchecked{return (Level*397^X)*397^Y;}}
        }
        sealed class Tile : IDisposable {
            public readonly Key Key;
            public readonly RectangleF Bounds;
            public RasterBlitter.Image Image;
            public Mark[] Marks;
            public bool Ready,ShowVector;
            public int Next;
            public long Use;
            public Tile(Key key) {Key=key;Bounds=World(key);}
            public void Clear(bool showVector) {
                if(Image!=null)Image.Dispose();
                Image=null;Marks=null;Ready=false;Next=0;ShowVector=showVector;
            }
            public void Dispose() {Clear(false);}
        }
        static float Scale(int level) {return level>=1000?(level-1000)/512f:(float)Math.Pow(2,level/2.0);}
        static RectangleF World(Key key) {
            float span=Pixels/Scale(key.Level);
            return new RectangleF(key.X*span,key.Y*span,span,span);
        }
        static IEnumerable<Key> Keys(RectangleF view,int level) {
            float factor=Scale(level)/Pixels;
            int x0=(int)Math.Floor(view.Left*factor),x1=(int)Math.Floor((view.Right-.0001f)*factor);
            int y0=(int)Math.Floor(view.Top*factor),y1=(int)Math.Floor((view.Bottom-.0001f)*factor);
            for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)yield return new Key(level,x,y);
        }
        public void Reset(SpatialIndex scene,Action<Graphics,Mark,RectangleF> renderer) {
            Clear();index=scene;paint=renderer;
        }
        public void Reindex(SpatialIndex scene) {index=scene;}
        public void Clear() {
            foreach(var tile in tiles.Values)tile.Dispose();
            tiles.Clear();work.Clear();initialized=false;requestChanged=true;tick=0;
        }
        public void Dispose() {Clear();compositor.Dispose();}
        public void Invalidate(RectangleF bounds) {
            bounds.Inflate(12,12);
            foreach(var tile in tiles.Values)
                if(tile.Bounds.IntersectsWith(bounds)) {
                    tile.Clear(tile.Ready||tile.Image!=null||tile.ShowVector);
                    requestChanged=true;
                }
        }
        public void Request(RectangleF visible,float pixelsPerUnit,bool motion) {
            // Quantization avoids rebuilding while the wheel is between adjacent levels.
            int wanted=motion?(int)Math.Ceiling(Math.Log(Math.Max(.1f,pixelsPerUnit),2)*2-.025):1000+Math.Max(1,(int)Math.Round(pixelsPerUnit*512));
            if(initialized&&viewport==visible&&level==wanted&&!requestChanged)return;
            if(!initialized){initialized=true;level=previous=wanted;}
            if(level!=wanted){if(work.Count==0||previous==level)previous=level;level=wanted;}
            requestChanged=false;
            viewport=visible;work.Clear();
            float cx=visible.X+visible.Width/2,cy=visible.Y+visible.Height/2;
            var warm=visible;warm.Inflate(Pixels/Scale(level)*.45f,Pixels/Scale(level)*.45f);
            foreach(var key in Keys(warm,level)) {
                Tile tile;
                if(!tiles.TryGetValue(key,out tile))tiles.Add(key,tile=new Tile(key));
                tile.Use=++tick;
                if(!tile.Ready)work.Add(tile);
            }
            if(work.Count>1)work.Sort((a,b)=>{
                int priority=b.Bounds.IntersectsWith(visible).CompareTo(a.Bounds.IntersectsWith(visible));
                return priority==0?Distance(a.Bounds,cx,cy).CompareTo(Distance(b.Bounds,cx,cy)):priority;
            });
            if(tiles.Count>Capacity) {
                var old=tiles.Values.Where(t=>t.Key.Level!=level||!t.Bounds.IntersectsWith(warm))
                    .OrderBy(t=>t.Use).Take(tiles.Count-Capacity).ToArray();
                foreach(var tile in old){tiles.Remove(tile.Key);tile.Dispose();}
            }
        }
        static float Distance(RectangleF r,float x,float y) {return Math.Abs(r.X+r.Width/2-x)+Math.Abs(r.Y+r.Height/2-y);}
        public bool Pending {get {return work.Count>0;}}
        public RectangleF Pump(double milliseconds) {
            if(work.Count==0)return RectangleF.Empty;
            var clock=Stopwatch.StartNew();
            var dirty=RectangleF.Empty;
            do {
                var tile=work[0];
                Build(tile,clock,milliseconds);
                if(tile.Ready||tile.Image!=null&&!tile.ShowVector&&level==previous)
                    dirty=dirty.IsEmpty?tile.Bounds:RectangleF.Union(dirty,tile.Bounds);
                if(tile.Ready) {
                    work.RemoveAt(0);
                }
                // A heavily populated tile can continue building across ticks.
            } while(work.Count>0&&clock.Elapsed.TotalMilliseconds<milliseconds);
            return dirty;
        }
        void Build(Tile tile,Stopwatch clock,double milliseconds) {
            float scale=Scale(tile.Key.Level);
            var region=tile.Bounds;
            region.Inflate(Gutter/scale,Gutter/scale);
            if(tile.Marks==null) {
                var marks=index.Query(region).ToArray();
                if(marks.Length==0) {tile.Ready=true;tile.ShowVector=false;return;}
                // Both code paths have identical layer order.
                tile.Marks=marks.Where(m=>m.Kind=="frame").Concat(marks.Where(m=>m.Kind!="frame")).ToArray();
                tile.Image=new RasterBlitter.Image(Pixels+2*Gutter,Pixels+2*Gutter);
            }
            using(var g=Graphics.FromImage(tile.Image.Bitmap)) {
                Art.Prepare(g);
                g.TranslateTransform(Gutter-tile.Bounds.X*scale,Gutter-tile.Bounds.Y*scale);
                g.ScaleTransform(scale,scale);
                do {
                    paint(g,tile.Marks[tile.Next++],region);
                } while(tile.Next<tile.Marks.Length&&clock.Elapsed.TotalMilliseconds<milliseconds);
            }
            if(tile.Next==tile.Marks.Length) {
                tile.Ready=true;tile.ShowVector=false;tile.Marks=null;
            }
        }
        static void AddPatch(List<RasterBlitter.Patch> patches,Tile tile,RectangleF clip) {
            if(tile.Image!=null)patches.Add(new RasterBlitter.Patch{Native=tile.Image,Bitmap=tile.Image.Bitmap,Destination=tile.Bounds,Clip=clip});
        }
        public void Draw(Graphics g,RectangleF view,Func<RectangleF,bool> altered,Action<Graphics,RectangleF> direct) {
            var patches=new List<RasterBlitter.Patch>();
            var vector=new List<RectangleF>();
            foreach(var key in Keys(view,level)) {
                Tile tile;
                if(!tiles.TryGetValue(key,out tile))continue;
                var visible=RectangleF.Intersect(tile.Bounds,view);
                if(visible.IsEmpty)continue;
                if(altered(visible)||tile.ShowVector) {
                    vector.Add(visible);
                } else if(tile.Ready||previous==level)AddPatch(patches,tile,RectangleF.Empty);
                else if(previous!=level) {
                    bool found=false;
                    foreach(var oldKey in Keys(visible,previous)) {
                        Tile old;
                        if(tiles.TryGetValue(oldKey,out old)&&old.Ready&&!old.ShowVector) {old.Use=++tick;AddPatch(patches,old,visible);found=true;}
                    }
                    if(!found)AddPatch(patches,tile,RectangleF.Empty);
                }
            }
            compositor.Draw(g,patches,view,Source);
            foreach(var rect in vector) {
                var state=g.Save();g.SetClip(rect,CombineMode.Intersect);
                direct(g,rect);g.Restore(state);
            }
        }
    }
}
