using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Polya {
    sealed class TextLayout {
        static readonly ConditionalWeakTable<Mark,TextLayout> layouts=new ConditionalWeakTable<Mark,TextLayout>();
        static readonly Bitmap measurement=new Bitmap(1,1);
        readonly List<string> lines=new List<string>();
        readonly float size,lineHeight;
        readonly float width;
        string[] paragraphs;
        int paragraphIndex,offset;
        TextLayout(Mark mark) {
            size=mark.Kind=="note"?15:16; lineHeight=size*1.44f;
            width=mark.W-(mark.Kind=="note"?38:42);
            paragraphs=(mark.Text??"").Replace("\r\n","\n").Replace('\r','\n').Split('\n');
        }
        void Ensure(int lineCount) {
            if(paragraphs==null||lines.Count>=lineCount)return;
            using(var g=Graphics.FromImage(measurement)) {
                var font=Art.NoteFont(size);
                int maxLength=Math.Max(16,(int)(width/size*4));
                while(paragraphIndex<paragraphs.Length&&lines.Count<lineCount) {
                    string paragraph=paragraphs[paragraphIndex];
                    int start=offset,remaining=paragraph.Length-start;
                    if(remaining==0||(remaining<=maxLength&&g.MeasureString(start==0?paragraph:paragraph.Substring(start),font).Width<=width)) {
                        lines.Add(start==0?paragraph:paragraph.Substring(start));
                        paragraphIndex++;offset=0;continue;
                    }
                    int low=1,high=Math.Min(remaining,maxLength);
                    while(low<high) {
                        int mid=(low+high+1)/2;
                        if(g.MeasureString(paragraph.Substring(start,mid),font).Width<=width) low=mid; else high=mid-1;
                    }
                    int end=start+low;
                    if(end<paragraph.Length) {
                        int space=paragraph.LastIndexOf(' ',end-1,low);
                        if(space>start&&paragraph[end]!=' ') end=space;
                    }
                    if(end<paragraph.Length&&Char.IsHighSurrogate(paragraph[end-1])&&Char.IsLowSurrogate(paragraph[end]))
                        end=end-1>start?end-1:end+1;
                    lines.Add(paragraph.Substring(start,end-start).TrimEnd());
                    while(end<paragraph.Length&&paragraph[end]==' ')end++;
                    if(end==paragraph.Length){paragraphIndex++;offset=0;}
                    else offset=end;
                }
                if(paragraphIndex==paragraphs.Length)paragraphs=null;
            }
        }
        static TextLayout For(Mark mark) { return layouts.GetValue(mark,m=>new TextLayout(m)); }
        public static void Fit(Mark mark) {
            var text=For(mark);
            text.Ensure(Int32.MaxValue);
            mark.H=Math.Max(mark.Kind=="note"?125:92,(String.IsNullOrEmpty(mark.Title)?38:69)+text.lines.Count*text.lineHeight);
        }
        public static void Draw(Graphics g,Mark mark,float x,float y,float width,RectangleF view,Color color) {
            var layout=For(mark);
            int start=Math.Max(0,(int)Math.Floor((view.Top-y)/layout.lineHeight));
            int limit=(int)Math.Ceiling((view.Bottom-y)/layout.lineHeight)+1;
            if(limit<=start)return;
            layout.Ensure(limit);
            int end=Math.Min(layout.lines.Count,limit);
            var font=Art.NoteFont(layout.size);
            for(int i=start;i<end;i++) Art.Label(g,layout.lines[i],x,y+i*layout.lineHeight,width,layout.lineHeight,layout.size,color,face:font);
        }
    }

    static class GraphArt {
        public static bool UsesParameter(string formula) {return Regex.IsMatch(formula??"",@"(?<![\p{L}])a(?![\p{L}])",RegexOptions.IgnoreCase);}
        sealed class Curve {
            public readonly double[] Values=new double[601];
            public readonly double YMax;
            public readonly bool Valid;
            public Curve(Mark model) {
                try {
                    var evaluate=Formula.Compile(model.Text,parameter:model.Parameter??1);
                    var baseline=UsesParameter(model.Text)?Formula.Compile(model.Text,parameter:1):evaluate;
                    var magnitudes=new List<double>();
                    bool valid=false;
                    for(int i=0;i<Values.Length;i++) {
                        double x=model.From+(model.To-model.From)*i/(Values.Length-1);
                        var y=baseline(x);
                        Values[i]=evaluate(x);
                        valid|=!Double.IsInfinity(Values[i])&&!Double.IsNaN(Values[i]);
                        if(!Double.IsInfinity(y)&&!Double.IsNaN(y)) magnitudes.Add(Math.Abs(y));
                    }
                    magnitudes.Sort();
                    YMax=magnitudes.Count>0?Math.Max(1.5,Math.Min(1e100,magnitudes[(int)((magnitudes.Count-1)*.94)]*1.18)):1.5;
                    Valid=valid;
                } catch(FormatException) { YMax=1.5; }
            }
        }
        static readonly ConditionalWeakTable<Mark,Curve> cache=new ConditionalWeakTable<Mark,Curve>();
        public static void Plot(Graphics g,RectangleF pane,Mark model,Color lineColor) {
            var curve=cache.GetValue(model,m=>new Curve(m));
            var area=new RectangleF(pane.X+44,pane.Y+14,pane.Width-60,pane.Height-37);
            if(area.Width<=0||area.Height<=0) return;
            double width=model.To-model.From;
            if(Double.IsNaN(width)||Double.IsInfinity(width)||width<=0) return;
            var grid=Art.C("#E8E5F1"); var axes=Art.C("#ABA5C0");
            for(int i=0;i<=6;i++) {
                float x=area.X+area.Width*i/6;
                Art.Stroke(g,grid,1,x,area.Top,x,area.Bottom);
                Art.Label(g,(model.From+width*i/6).ToString("G3",CultureInfo.InvariantCulture),x-24,area.Bottom+4,48,16,10,Art.Fade,false,StringAlignment.Center,true);
            }
            for(int i=0;i<=4;i++) {
                float y=area.Y+area.Height*i/4;
                Art.Stroke(g,i==2?axes:grid,1,area.Left,y,area.Right,y);
                Art.Label(g,(curve.YMax*(1-i*.5)).ToString("G2",CultureInfo.InvariantCulture),pane.Left+1,y-7,40,14,10,Art.Fade,false,StringAlignment.Far,true);
            }
            if(model.From<=0&&model.To>=0) {
                float x=area.Left+(float)(-model.From/width)*area.Width;
                Art.Stroke(g,axes,1,x,area.Top,x,area.Bottom);
            }
            if(!curve.Valid) {
                Art.Label(g,"Нет значений на этом интервале",area.X,area.Y,area.Width,area.Height,13,Art.Muted,false,StringAlignment.Center);
                return;
            }
            var state=g.Save(); g.SetClip(area,CombineMode.Intersect);
            using(var pen=Art.Pen(lineColor,2.65f)) {
                var segment=new List<PointF>();
                foreach(var value in curve.Values.Select((y,i)=>new {Y=y/curve.YMax,I=i})) {
                    float y=(float)(area.Top+area.Height*(.5-.5*value.Y));
                    if(Double.IsNaN(value.Y)||Double.IsInfinity(value.Y)||Math.Abs(value.Y)>10||(segment.Count>0&&Math.Abs(segment.Last().Y-y)>area.Height*.75)) {
                        if(segment.Count>1) g.DrawLines(pen,segment.ToArray());
                        segment.Clear();
                    }
                    if(!Double.IsNaN(value.Y)&&!Double.IsInfinity(value.Y)&&Math.Abs(value.Y)<=10) segment.Add(new PointF(area.X+area.Width*value.I/(curve.Values.Length-1),y));
                }
                if(segment.Count>1) g.DrawLines(pen,segment.ToArray());
            }
            g.Restore(state);
        }
        public static void Circle(Graphics g,RectangleF pane,double angle) {
            float cx=pane.Left+pane.Width/2,cy=pane.Top+pane.Height/2;
            float radius=Math.Min(pane.Width,pane.Height)/2-22;
            if(radius<=0) return;
            double rad=angle*Math.PI/180;
            float x=cx+(float)Math.Cos(rad)*radius,y=cy-(float)Math.Sin(rad)*radius;
            Art.Stroke(g,Art.Line,1,cx-radius-12,cy,cx+radius+12,cy);
            Art.Stroke(g,Art.Line,1,cx,cy-radius-12,cx,cy+radius+12);
            Art.Circle(g,Art.C("#BBB2DC"),cx,cy,radius,1.4f);
            for(int i=0;i<12;i++) {
                double tick=i*Math.PI/6;
                Art.Dot(g,Art.C("#B8ABD9"),cx+(float)Math.Cos(tick)*radius,cy-(float)Math.Sin(tick)*radius,2);
            }
            using(var dashed=Art.Pen(Art.C("#A999E8"),1.5f)) {
                dashed.DashStyle=DashStyle.Dash; g.DrawLine(dashed,x,y,x,cy); g.DrawLine(dashed,x,y,cx,y);
            }
            Art.Stroke(g,Art.Purple,2.5f,cx,cy,x,y); Art.Dot(g,Art.Purple,x,y,5);
            Art.Label(g,"x",cx+radius+10,cy-18,13,16,11,Art.Muted,false,StringAlignment.Center,true);
            Art.Label(g,"y",cx+6,cy-radius-19,13,16,11,Art.Muted,false,StringAlignment.Center,true);
        }
    }

    sealed partial class Surface {
        const int HistoryLimit=150;
        readonly Dictionary<Board,History> histories=new Dictionary<Board,History>();
        sealed class Edit { public Mark Before,After; public int At; public Edit[] Batch; }
        sealed class History {
            public readonly Stack<Edit> Undo=new Stack<Edit>(), Redo=new Stack<Edit>();
        }
        public Board Board { get; private set; }
        public Mark Selected { get; private set; }
        public Mark TextBeingEdited;
        public RectangleF ScreenRect(RectangleF r) {
            return new RectangleF(Area.X+Board.ViewX+r.X*Board.Zoom,Area.Y+Board.ViewY+r.Y*Board.Zoom,r.Width*Board.Zoom,r.Height*Board.Zoom);
        }
        public RectangleF Area;
        public string Tool="select";
        public string Color="#5F43CD";
        public string NoteColor="#FFF1C3";
        public float Weight=2.5f;
        public string Fill="", Dash="";
        public int Opacity=100;
        public bool KeepTool;
        public bool Tidy { get { return Board.Straighten??true; } set { Board.Straighten=value; } }
        readonly HashSet<Mark> erased=new HashSet<Mark>();
        PointF erasePoint;
        int vertex=-1;
        RectangleF eraseDamage;
        static readonly ConditionalWeakTable<Mark,PointF[]> inkPoints=new ConditionalWeakTable<Mark,PointF[]>();
        public bool Space;
        SpatialIndex index;
        SceneTiles raster;
        readonly BoardGrid grid=new BoardGrid();
        public float DeviceScale=1;
        public bool AnimatingZoom;
        public bool MoreTiles {get {return raster!=null&&raster.Pending;}}
        public void UseRaster() {
            if(raster!=null)return;
            raster=new SceneTiles();raster.Reset(index,DrawMark);
        }
        public void ReleaseRaster() {
            if(raster!=null){raster.Dispose();raster=null;}
            grid.Dispose();
        }
        public RectangleF PrepareTiles(double milliseconds) {
            if(!MoreTiles)return RectangleF.Empty;
            var ready=raster.Pump(milliseconds);
            if(ready.IsEmpty)return ready;
            return new RectangleF(Area.X+Board.ViewX+ready.X*Board.Zoom,Area.Y+Board.ViewY+ready.Y*Board.Zoom,
                ready.Width*Board.Zoom,ready.Height*Board.Zoom);
        }
        readonly List<PointF> stroke=new List<PointF>();
        sealed class StrokeSlice : IDisposable {
            public readonly GraphicsPath Path;
            public readonly RectangleF Bounds;
            public StrokeSlice(GraphicsPath path,float weight) {
                Path=path;var r=path.GetBounds();r.Inflate(weight+2,weight+2);Bounds=r;
            }
            public void Dispose() {Path.Dispose();}
        }
        readonly List<StrokeSlice> drawnSlices=new List<StrokeSlice>();
        int sealedSegments;
        PointF start,panStart;
        Mark ghost;
        string drag;
        public event Action Changed;
        public event Action<Mark> EditMark, PreviewLink, RevealTask;
        public bool CanUndo { get { return histories[Board].Undo.Count>0; } }
        public bool CanRedo { get { return histories[Board].Redo.Count>0; } }
        public bool Dragging { get { return drag!=null; } }
        public void Reset() { histories.Clear(); }
        public void Forget(Board board) { histories.Remove(board); }
        public void Bind(Board board) {
            Board=board; Selected=null; Cancel(); index=new SpatialIndex();
            if(!histories.ContainsKey(board)) histories[board]=new History();
            foreach(var m in board.Marks) index.Add(m);
            if(raster!=null)raster.Reset(index,DrawMark);
        }
        public void Cancel() {
            drag=null;stroke.Clear();pressures.Clear();erased.Clear();cuts.Clear();eraseTail.Clear();ghost=null;sealedSegments=0;
            foreach(var slice in drawnSlices)slice.Dispose();
            drawnSlices.Clear();
        }
        public PointF World(PointF pointer) { return new PointF((pointer.X-Area.X-Board.ViewX)/Board.Zoom,(pointer.Y-Area.Y-Board.ViewY)/Board.Zoom); }
        public PointF Center() { return World(new PointF(Area.X+Area.Width/2,Area.Y+Area.Height/2)); }
        public void ZoomAt(float multiplier,PointF cursor) {
            PointF at=World(cursor);
            Board.Zoom=Math.Min(4,Math.Max(.15f,Board.Zoom*multiplier));
            Board.ViewX=cursor.X-Area.X-at.X*Board.Zoom;
            Board.ViewY=cursor.Y-Area.Y-at.Y*Board.Zoom;
        }
        public void ResetZoom() { PointF center=new PointF(Area.X+Area.Width/2,Area.Y+Area.Height/2); ZoomAt(1/Board.Zoom,center); }
        public void Fit() {
            if(Board.Marks.Count==0) { Board.Zoom=1; Board.ViewX=Board.ViewY=0; return; }
            var rect=Board.Marks.Select(m=>m.Bounds).Aggregate(RectangleF.Union);
            Focus(rect,80,125);
        }
        public void Focus(RectangleF rect,float horizontal,float vertical) {
            Board.Zoom=Math.Max(.15f,Math.Min(4,Math.Min((Area.Width-horizontal)/Math.Max(1,rect.Width),(Area.Height-vertical)/Math.Max(1,rect.Height))));
            Board.ViewX=(Area.Width-rect.Width*Board.Zoom)/2-rect.X*Board.Zoom;
            Board.ViewY=(Area.Height-rect.Height*Board.Zoom)/2-rect.Y*Board.Zoom+10;
        }
        void Record(Mark before,Mark after,int at) {
            var edit=new Edit{Before=before,After=after,At=at};
            Push(edit); Apply(before,after,at);
        }
        public int Revision {get;private set;}
        void Push(Edit edit) {
            Revision++;
            var history=histories[Board];
            if(history.Undo.Count>=HistoryLimit) {
                var keep=history.Undo.Take(HistoryLimit-1).Reverse().ToArray();
                history.Undo.Clear(); foreach(var item in keep) history.Undo.Push(item);
            }
            history.Undo.Push(edit); history.Redo.Clear();
        }
        void Apply(Mark before,Mark after,int at) {
            if(before==null) {
                bool append=at==Board.Marks.Count;
                Board.Marks.Insert(at,after);
                if(append) index.Add(after); else Rebuild();
            }
            else if(after==null) { Board.Marks.RemoveAt(at); index.Remove(before); }
            else { Board.Marks[at]=after; index.Replace(before,after); }
            if(raster!=null) {
                if(before!=null)raster.Invalidate(VisualBounds(before));
                if(after!=null)raster.Invalidate(VisualBounds(after));
            }
            Selected=after; if(Changed!=null) Changed();
        }
        void Rebuild() {
            index=new SpatialIndex(); foreach(var mark in Board.Marks) index.Add(mark);
            if(raster!=null)raster.Reindex(index);
        }
        public void Undo(bool redo) {
            if(Dragging) return;
            var history=histories[Board];
            var from=redo?history.Redo:history.Undo;
            if(from.Count==0) return;
            var edit=from.Pop(); (redo?history.Undo:history.Redo).Push(edit);
            if(edit.Batch!=null) ApplyErased(edit.Batch,redo);
            else Apply(redo?edit.Before:edit.After,redo?edit.After:edit.Before,edit.At);
        }
        void ApplyErased(Edit[] batch,bool remove) {
            if(batch[0].After!=null) {
                foreach(var edit in batch) {
                    var old=remove?edit.Before:edit.After; var next=remove?edit.After:edit.Before;
                    Board.Marks[edit.At]=next;index.Replace(old,next);
                    if(raster!=null) raster.Invalidate(VisualBounds(old));
                }
                Selected=null;if(Changed!=null) Changed();return;
            }
            if(remove) {
                var items=new HashSet<Mark>(batch.Select(e=>e.Before));
                Board.Marks.RemoveAll(items.Contains);
                foreach(var mark in items) index.Remove(mark);
            } else {
                foreach(var e in batch) Board.Marks.Insert(e.At,e.Before);
                Rebuild();
            }
            if(raster!=null) {
                foreach(var item in batch)raster.Invalidate(VisualBounds(item.Before));
            }
            Selected=null; if(Changed!=null) Changed();
        }
        void Sweep(PointF end) {
            float radius=10/Board.Zoom;
            int steps=Math.Max(1,(int)Math.Ceiling(Ink.Distance(erasePoint,end)/(radius*.65)));
            var sweep=RectangleF.FromLTRB(Math.Min(erasePoint.X,end.X),Math.Min(erasePoint.Y,end.Y),Math.Max(erasePoint.X,end.X),Math.Max(erasePoint.Y,end.Y));
            sweep.Inflate(radius,radius);
            foreach(var mark in index.Query(sweep)) {
                if(drag=="pixel") {
                    if(mark.Kind=="frame"||!VisualBounds(mark).IntersectsWith(sweep)) continue;
                    List<float> strip;
                    if(!cuts.TryGetValue(mark,out strip)) cuts[mark]=strip=new List<float>();
                    strip.AddRange(new[]{erasePoint.X-mark.X,erasePoint.Y-mark.Y,end.X-mark.X,end.Y-mark.Y,radius});
                    continue;
                }
                if(erased.Contains(mark))continue;
                var bounds=VisualBounds(mark);
                bounds.Inflate(radius,radius);
                for(int i=1;i<=steps;i++) {
                    var p=new PointF(erasePoint.X+(end.X-erasePoint.X)*i/steps,erasePoint.Y+(end.Y-erasePoint.Y)*i/steps);
                    if(!bounds.Contains(p)||!Touches(mark,p,radius))continue;
                    erased.Add(mark);eraseDamage=Merge(eraseDamage,ScreenBounds(mark));break;
                }
            }
            if(eraseTail.Count==0||Ink.Distance(eraseTail[eraseTail.Count-1],end)*Board.Zoom>=1) {
                if(eraseTail.Count>=24) eraseTail.RemoveAt(0);
                eraseTail.Add(end);
            }
            erasePoint=end;
        }
        public void Add(Mark mark) { Record(null,mark,Board.Marks.Count); }
        public void Replace(Mark before,Mark after) { Record(before,after,Board.Marks.IndexOf(before)); }
        public void Delete() { if(Selected!=null) Record(Selected,null,Board.Marks.IndexOf(Selected)); }
        public void ClearSelection() { Selected=null; }
        public static bool Shape(string kind) {return kind=="ellipse"||kind=="rect"||kind=="diamond";}
        public static bool Outline(string kind) {return Shape(kind)||kind=="ink"||kind=="pen"||kind=="line"||kind=="arrow";}
        public void Style(Action<Mark> update) {
            if(Selected==null) return;
            var next=Selected.Copy(); update(next); Replace(Selected,next);
        }
        void SetStyle(Mark mark) {mark.Fill=Fill;mark.Dash=Dash;mark.Opacity=Opacity;}
        public void OnDown(PointF position,bool pan,bool shift) {
            start=World(position);
            if(pan||Space||Tool=="hand") { drag="pan"; panStart=position; return; }
            if(IsEraser(Tool)) { Selected=null; drag=Tool; erasePoint=start; Sweep(start); return; }
            if(Tool=="select") {
                var hit=Hit(start);
                if(hit!=null&&hit.Kind=="trig"&&Ink.Distance(TrigPoint(hit),start)*Board.Zoom<16) {
                    Selected=hit;ghost=hit.Copy();drag="trig-angle";return;
                }
                vertex=DiagramEdit.HandleAt(Selected,start,9/Board.Zoom);
                if(vertex>=0) {
                    drag="vertex";ghost=Selected.Copy();DiagramEdit.BeginMove(ghost);return;
                }
                Selected=hit;
                if(Selected!=null) {
                    if(shift&&Selected.Kind=="link") { if(PreviewLink!=null) PreviewLink(Selected); return; }
                    drag="move"; ghost=Selected.Copy();
                }
                return;
            }
            if(Tool=="text"||Tool=="note"||Tool=="link") {
                var mark=Mark.Make(Tool,start.X,start.Y,Tool=="note"?256:Tool=="link"?285:440,Tool=="link"?94:125,color:Tool=="note"?NoteColor:"");
                if(EditMark!=null) EditMark(mark);
                return;
            }
            Selected=null; drag=Tool;
            if(drag=="pen") {stroke.Add(start);pressures.Add(PenPressure);}
        }
        public void OnMove(PointF position,bool shift) {
            if(drag==null) return;
            if(drag=="pan") {
                Board.ViewX+=position.X-panStart.X; Board.ViewY+=position.Y-panStart.Y; panStart=position; return;
            }
            PointF at=World(position);
            if(IsEraser(drag)) { if(at!=erasePoint) Sweep(at); return; }
            if(drag=="trig-angle") {
                var center=TrigCenter(ghost);
                double angle=Math.Atan2(center.Y-at.Y,at.X-center.X)*180/Math.PI;
                ghost.Angle=(angle+360)%360;
                if(shift)ghost.Angle=Math.Round(ghost.Angle/15)*15%360;
                return;
            }
            if(drag=="move") { ghost.X=Selected.X+at.X-start.X; ghost.Y=Selected.Y+at.Y-start.Y; return; }
            if(drag=="vertex") {if(Ink.Distance(at,start)*Board.Zoom>.8)DiagramEdit.Move(ghost,vertex,at,shift);return;}
            if(drag=="pen") {
                if(Ink.Distance(at,stroke[stroke.Count-1])*Board.Zoom>=(PenPressure<1?.45:1.0)) {stroke.Add(at);pressures.Add(PenPressure);}
                return;
            }
            if(drag=="line"||drag=="arrow") {
                if(shift) {
                    double d=Ink.Distance(at,start), angle=Math.Round(Math.Atan2(at.Y-start.Y,at.X-start.X)/(Math.PI/4))*Math.PI/4;
                    at=new PointF(start.X+(float)(d*Math.Cos(angle)),start.Y+(float)(d*Math.Sin(angle)));
                }
                ghost=Ink.Line(drag,start,at,Color,Weight); SetStyle(ghost); return;
            }
            float dx=at.X-start.X,dy=at.Y-start.Y;
            if(shift) { float length=Math.Max(Math.Abs(dx),Math.Abs(dy)); dx=dx<0?-length:length; dy=dy<0?-length:length; }
            ghost=Mark.Make(drag,start.X+Math.Min(0,dx),start.Y+Math.Min(0,dy),Math.Abs(dx),Math.Abs(dy),color:Color);
            ghost.Size=Weight; SetStyle(ghost);
        }
        static RectangleF Merge(RectangleF a,RectangleF b) {return a.IsEmpty?b:b.IsEmpty?a:RectangleF.Union(a,b);}
        static RectangleF VisualBounds(Mark mark) {return SpatialIndex.Area(mark);}
        RectangleF ScreenBounds(Mark mark) {
            if(mark==null) return RectangleF.Empty;
            float z=Board.Zoom;
            var r=new RectangleF(Area.X+Board.ViewX+mark.X*z,Area.Y+Board.ViewY+mark.Y*z,mark.W*z,mark.H*z);
            r.Inflate(14+mark.Size*z,14+mark.Size*z);
            return r;
        }
        RectangleF TailBounds() {
            if(stroke.Count==0)return RectangleF.Empty;
            int start=Math.Max(0,stroke.Count-6);
            float x=stroke[start].X,y=stroke[start].Y,l=x,r=x,t=y,b=y;
            for(int i=start+1;i<stroke.Count;i++) {
                var p=stroke[i];l=Math.Min(l,p.X);r=Math.Max(r,p.X);t=Math.Min(t,p.Y);b=Math.Max(b,p.Y);
            }
            var bounds=new RectangleF(Area.X+Board.ViewX+l*Board.Zoom,Area.Y+Board.ViewY+t*Board.Zoom,(r-l)*Board.Zoom,(b-t)*Board.Zoom);
            bounds.Inflate(12+Weight*Board.Zoom,12+Weight*Board.Zoom);
            return bounds;
        }
        public RectangleF Track(PointF position,bool shift) {
            if(drag=="pan") {OnMove(position,shift);return Area;}
            var before=IsEraser(drag)?EraserBounds():drag=="pen"?TailBounds():ScreenBounds(ghost);
            eraseDamage=RectangleF.Empty;
            OnMove(position,shift);
            return Merge(Merge(before,IsEraser(drag)?EraserBounds():drag=="pen"?TailBounds():ScreenBounds(ghost)),eraseDamage);
        }
        public void OnUp(PointF position,bool shift) {
            if(drag==null) return;
            OnMove(position,shift);
            if(drag=="eraser"&&erased.Count>0) {
                var batch=Board.Marks.Select((m,i)=>erased.Contains(m)?new Edit{Before=m,At=i}:null).Where(e=>e!=null).ToArray();
                Push(new Edit{Batch=batch}); ApplyErased(batch,true);
            } else if(drag=="pixel"&&cuts.Count>0) {
                var batch=Board.Marks.Select((before,i)=>cuts.ContainsKey(before)?new Edit{Before=before,At=i}:null)
                    .Where(edit=>edit!=null).ToArray();
                foreach(var edit in batch) {
                    edit.After=edit.Before.Copy();
                    edit.After.Cuts=(edit.Before.Cuts??new float[0]).Concat(cuts[edit.Before]).ToArray();
                }
                Push(new Edit{Batch=batch});ApplyErased(batch,true);
            } else if(drag=="trig-angle") {
                if(Ink.Distance(start,World(position))*Board.Zoom>1)Replace(Selected,ghost);
            } else if(drag=="vertex") {
                if(Ink.Distance(start,World(position))*Board.Zoom>.8) Replace(Selected,ghost);
            } else if(drag=="move"&&Ink.Distance(start,World(position))*Board.Zoom>2) Replace(Selected,ghost);
            else if(drag=="pen") {
                var mark=Ink.Finish(stroke,Color,Weight,Tidy&&!PenContact);
                if(mark.Kind=="ink"&&pressures.Any(p=>p<1)) mark.Pressure=pressures.ToArray();
                SetStyle(mark); Add(mark);
            } else if(drag!="move"&&ghost!=null&&Math.Max(ghost.W,ghost.H)*Board.Zoom>3) {
                if(!KeepTool) Tool="select";
                Add(ghost);
            }
            Cancel();
        }
        public void OnDouble(PointF position) {
            if(Tool!="select") return;
            Cancel();
            var m=Hit(World(position)); if(m==null) return;
            Selected=m;
            if(m.Kind=="link") { if(PreviewLink!=null) PreviewLink(m); }
            else if(m.Kind=="task") {if(RevealTask!=null)RevealTask(m);}
            else if(EditMark!=null) EditMark(m);
        }
        static PointF TrigCenter(Mark mark) {
            return new PointF(mark.X+mark.W/2,mark.Y+44+(mark.H-83)/2);
        }
        static PointF TrigPoint(Mark mark) {
            var center=TrigCenter(mark);
            var r=Math.Max(0,Math.Min(mark.W-24,mark.H-83)/2-22);
            var a=mark.Angle*Math.PI/180;
            return new PointF(center.X+(float)Math.Cos(a)*r,center.Y-(float)Math.Sin(a)*r);
        }
        Mark Hit(PointF p) {
            float tolerance=7/Board.Zoom;
            return index.Query(new RectangleF(p.X-tolerance,p.Y-tolerance,tolerance*2,tolerance*2)).Reverse().FirstOrDefault(m=>Touches(m,p,tolerance));
        }
        static bool Touches(Mark m,PointF p,float tolerance) {
                if(m.Kind=="ink"||m.Kind=="arrow"||m.Kind=="line") {
                    if(m.Points==null||m.Points.Length<2) return false;
                    var last=new PointF(m.X+m.Points[0],m.Y+m.Points[1]);
                    if(m.Points.Length==2) return Ink.Distance(p,last)<=tolerance+m.Size;
                    for(int i=2;i<m.Points.Length;i+=2) {
                        var next=new PointF(m.X+m.Points[i],m.Y+m.Points[i+1]);
                        if(Ink.Segment(p,last,next)<=tolerance+m.Size) return true;
                        last=next;
                    }
                    return false;
                }
                var rect=m.Bounds; rect.Inflate(tolerance,tolerance);
                if(m.Kind=="frame"&&rect.Contains(p)) return p.Y<m.Y+38||p.Y>m.Y+m.H-tolerance||p.X<m.X+tolerance||p.X>m.X+m.W-tolerance;
                return rect.Contains(p);
        }
        public void Draw(Graphics g) {
            var state=g.Save();
            g.SetClip(Area,CombineMode.Intersect);
            var clip=RectangleF.Intersect(Area,g.VisibleClipBounds);
            if(clip.Width<=0||clip.Height<=0) { g.Restore(state);return; }
            Color paper=Art.ColorOf(Board.Background,Art.White);
            string pattern=Board.Pattern??"grid";
            float spacing=(pattern=="grid"?11:22)*Board.Zoom;
            while(spacing<(pattern=="grid"?8:12)) spacing*=2;
            while(spacing>62) spacing/=2;
            if(pattern=="grid")grid.Draw(g,clip,paper,spacing,Area.X+Board.ViewX,Area.Y+Board.ViewY);
            else Art.Fill(g,paper,clip);
            if(pattern=="dots"||pattern=="ruled") {
                using(var dots=new SolidBrush(Art.C("#D3D7DF"))) {
                    float left=Area.Left+Board.ViewX;
                    float top=Area.Top+Board.ViewY;
                    int column=(int)Math.Floor((clip.Left-left)/spacing),row=(int)Math.Floor((clip.Top-top)/spacing);
                    left+=spacing*column;
                    top+=spacing*row;
                    if(pattern=="dots") {
                        for(float x=left;x<clip.Right+1;x+=spacing)
                            for(float y=top;y<clip.Bottom+1;y+=spacing) g.FillEllipse(dots,x-.9f,y-.9f,1.65f,1.65f);
                    } else using(var line=Art.Pen(System.Drawing.Color.FromArgb(47,92,96,109),.9f)) {
                        for(float y=top;y<clip.Bottom+1;y+=spacing)g.DrawLine(line,clip.Left,y,clip.Right,y);
                    }
                }
            }
            g.TranslateTransform(Area.X+Board.ViewX,Area.Y+Board.ViewY); g.ScaleTransform(Board.Zoom,Board.Zoom);
            var corner=World(clip.Location);
            var view=new RectangleF(corner.X,corner.Y,clip.Width/Board.Zoom,clip.Height/Board.Zoom);
            if(raster!=null&&Board.Marks.Count>=96) {
                var origin=World(Area.Location);
                raster.Request(new RectangleF(origin.X,origin.Y,Area.Width/Board.Zoom,Area.Height/Board.Zoom),Board.Zoom*DeviceScale,AnimatingZoom);
                raster.Draw(g,view,TransientIn,DrawCommitted);
            } else DrawCommitted(g,view);
            if(drag=="move"&&ghost!=null) {
                float dx=ghost.X-Selected.X,dy=ghost.Y-Selected.Y;
                var moved=g.Save(); g.TranslateTransform(dx,dy);
                var sourceView=view; sourceView.Offset(-dx,-dy);
                DrawMark(g,Selected,sourceView);
                g.Restore(moved);
            } else if(ghost!=null) DrawMark(g,ghost,view);
            if(stroke.Count>0) {
                var c=Fade(Art.ColorOf(Color,Art.Purple),Opacity);
                if(stroke.Count==1) Art.Dot(g,c,stroke[0].X,stroke[0].Y,Weight*pressures[0]/2);
                else if(PenContact||pressures[0]<1) DrawPressure(g,c,Weight,stroke,pressures,view);
                else using(var p=StyledPen(c,Weight,Dash)) DrawLiveInk(g,p,view);
            }
            DrawEraserTrail(g);
            var selection=drag=="move"||drag=="vertex"?ghost:Selected;
            if(selection!=null&&drag!="pen") {
                var box=selection.Bounds; box.Inflate(5/Board.Zoom,5/Board.Zoom);
                using(var pen=Art.Pen(Art.Purple,1.2f/Board.Zoom)) {
                    pen.DashStyle=DashStyle.Dash; g.DrawRectangle(pen,box.X,box.Y,box.Width,box.Height);
                }
                if(Tool=="select"&&selection.Kind=="diagram") DiagramEdit.DrawHandles(g,selection,Board.Zoom);
            }
            g.Restore(state);
        }
        bool TransientIn(RectangleF view) {
            if((drag=="move"||drag=="vertex"||drag=="trig-angle")&&Selected!=null&&VisualBounds(Selected).IntersectsWith(view))return true;
            if(TextBeingEdited!=null&&VisualBounds(TextBeingEdited).IntersectsWith(view))return true;
            if(drag=="pixel") foreach(var mark in cuts.Keys) if(VisualBounds(mark).IntersectsWith(view))return true;
            foreach(var mark in erased) if(VisualBounds(mark).IntersectsWith(view))return true;
            return false;
        }
        void DrawCommitted(Graphics g,RectangleF view) {
            var visible=index.Query(view).Where(m=>m!=TextBeingEdited&&((drag!="move"&&drag!="vertex"&&drag!="trig-angle")||Selected!=m)).ToArray();
            foreach(var mark in visible) if(mark.Kind=="frame") DrawDuringErase(g,mark,view);
            foreach(var mark in visible) if(mark.Kind!="frame") DrawDuringErase(g,mark,view);
        }
        static PointF[] Smooth(PointF[] points) {
            var a=points[0];
            for(int i=1;i<points.Length-1;i++) {
                var b=points[i]; var c=points[i+1];
                points[i]=new PointF((a.X+2*b.X+c.X)/4,(a.Y+2*b.Y+c.Y)/4);
                a=b;
            }
            return points;
        }
        GraphicsPath StrokePath(int first,int segments) {
            int start=Math.Max(0,first-2);
            int last=Math.Min(stroke.Count-1,first+segments+2);
            var sample=new PointF[last-start+1];
            stroke.CopyTo(start,sample,0,sample.Length);
            Smooth(sample);
            var path=new GraphicsPath();
            if(sample.Length==2)path.AddLine(sample[0],sample[1]);
            else path.AddCurve(sample,first-start,segments,.45f);
            return path;
        }
        void DrawLiveInk(Graphics g,Pen pen,RectangleF view) {
            if(!String.IsNullOrEmpty(Dash)) {DrawInk(g,pen,Smooth(stroke.ToArray()));return;}
            const int chunk=80;
            while(stroke.Count-1-sealedSegments>=chunk+2) {
                drawnSlices.Add(new StrokeSlice(StrokePath(sealedSegments,chunk),Weight));
                sealedSegments+=chunk;
            }
            foreach(var slice in drawnSlices)
                if(slice.Bounds.IntersectsWith(view))g.DrawPath(pen,slice.Path);
            int remaining=stroke.Count-1-sealedSegments;
            if(remaining>0)using(var path=StrokePath(sealedSegments,remaining))g.DrawPath(pen,path);
        }
        static void DrawInk(Graphics g,Pen pen,PointF[] points) {
            if(points.Length<3)g.DrawLines(pen,points);else g.DrawCurve(pen,points,.45f);
        }
        static PointF[] MarkPoints(Mark mark) {
            var points=new PointF[mark.Points.Length/2];
            for(int i=0;i<points.Length;i++) points[i]=new PointF(mark.X+mark.Points[2*i],mark.Y+mark.Points[2*i+1]);
            return mark.Kind=="ink"?Smooth(points):points;
        }
        static Color Fade(Color color,int opacity) {return System.Drawing.Color.FromArgb(color.A*opacity/100,color);}
        static Pen StyledPen(Color color,float weight,string dash) {
            var pen=Art.Pen(color,weight);
            if(dash=="dash") pen.DashStyle=DashStyle.Dash;
            if(dash=="dot") pen.DashStyle=DashStyle.Dot;
            return pen;
        }
        static void DrawMark(Graphics g,Mark m,RectangleF view) {
            if(m.Cuts==null||m.Cuts.Length==0) {DrawMarkCore(g,m,view);return;}
            var state=g.Save();
            ApplyCuts(g,m,m.Cuts,view);DrawMarkCore(g,m,view);g.Restore(state);
        }
        static void DrawMarkCore(Graphics g,Mark m,RectangleF view) {
            int opacity=m.Opacity??100;
            var color=Fade(Art.ColorOf(m.Color,Art.Purple),opacity);
            float x=m.X,y=m.Y,w=m.W,h=m.H;
            if(m.Kind=="frame"||m.Kind=="task"||m.Kind=="image") {LessonArt.Draw(g,m);return;}
            if(m.Kind=="diagram") {DiagramEdit.Draw(g,m);return;}
            if(m.Kind=="ink"||m.Kind=="line"||m.Kind=="arrow") {
                if(m.Points==null||m.Points.Length==0) return;
                var points=inkPoints.GetValue(m,MarkPoints);
                if(points.Length==1) Art.Dot(g,color,x+m.Points[0],y+m.Points[1],m.Size*(m.Pressure==null?1:m.Pressure[0])/2);
                else if(m.Pressure!=null) DrawPressure(g,color,m.Size,points,m.Pressure,view);
                else using(var pen=StyledPen(color,m.Size,m.Dash)) {
                    if(m.Kind=="arrow") pen.CustomEndCap=new AdjustableArrowCap(4,5);
                    if(m.Kind=="ink") DrawInk(g,pen,points); else g.DrawLines(pen,points);
                }
                return;
            }
            if(Shape(m.Kind)) {
                w=Math.Max(.1f,w);h=Math.Max(.1f,h);
                var fill=Fade(Art.ColorOf(m.Fill,System.Drawing.Color.Transparent),opacity);
                using(var p=StyledPen(color,m.Size,m.Dash)) using(var b=new SolidBrush(fill)) using(var figure=new GraphicsPath()) {
                    if(m.Kind=="ellipse") figure.AddEllipse(x,y,w,h);
                    else if(m.Kind=="diamond") figure.AddPolygon(new[]{new PointF(x+w/2,y),new PointF(x+w,y+h/2),new PointF(x+w/2,y+h),new PointF(x,y+h/2)});
                    else figure.AddRectangle(new RectangleF(x,y,w,h));
                    if(fill.A>0) g.FillPath(b,figure);
                    g.DrawPath(p,figure);
                }
                return;
            }
            if(m.Kind=="heading") {
                Art.Label(g,m.Title,x,y,w,53,40,Art.Ink,true);
                Art.Label(g,m.Text,x+2,y+55,w-4,24,15,Art.Muted);
                return;
            }
            if(m.Kind=="graph"||m.Kind=="trig") {
                Art.Label(g,m.Title,x+18,y+12,w-36,28,17,Art.Ink,true);
                var pad=new RectangleF(x+12,y+44,w-24,h-83);
                if(m.Kind=="graph") {
                    GraphArt.Plot(g,pad,m,color);
                    string formula="y = "+m.Text;
                    if(GraphArt.UsesParameter(m.Text))formula+="   ·   a = "+(m.Parameter??1).ToString("0.##",CultureInfo.CurrentCulture);
                    Art.Label(g,formula,x+22,y+h-35,w-44,24,14,color,true,mono:true);
                } else {
                    GraphArt.Circle(g,pad,m.Angle);
                    double a=m.Angle*Math.PI/180;
                    Art.Label(g,String.Format(CultureInfo.InvariantCulture,"{0:0.#}°   sin = {1:0.###}   cos = {2:0.###}",m.Angle,Math.Sin(a),Math.Cos(a)),x+18,y+h-36,w-36,24,14,color,true,StringAlignment.Center,true);
                }
                return;
            }
            if(m.Kind=="note"||m.Kind=="link") {
                Art.Round(g,new RectangleF(x,y+3,w,h),12,System.Drawing.Color.FromArgb(12,35,29,68));
                Art.Round(g,m.Bounds,12,m.Kind=="note"?Art.ColorOf(m.Color,Art.C("#FFF1C3")):Art.White,m.Kind=="note"?null:(Color?)Art.Line);
            }
            if(m.Kind=="link") {
                Art.Round(g,new RectangleF(x+15,y+19,30,30),8,Art.Pale);
                Art.Icon(g,"link",x+20,y+24,20,Art.Purple);
                Art.Icon(g,"external",x+w-36,y+23,17,Art.Muted);
                Art.Label(g,m.Title,x+53,y+16,w-92,26,14,Art.Ink,true);
                Uri uri; string site=Uri.TryCreate(m.Text,UriKind.Absolute,out uri)?uri.Host:m.Text;
                Art.Label(g,site,x+53,y+40,w-67,20,11,Art.Muted);
                Art.Label(g,"Двойной щелчок для просмотра",x+16,y+65,w-32,16,10,Art.Purple);
                return;
            }
            bool note=m.Kind=="note";
            float padX=note?19:21;
            if(!String.IsNullOrEmpty(m.Title)) Art.Label(g,m.Title,x+padX,y+13,w-padX*2,28,note?16:17,Art.Ink,true);
            TextLayout.Draw(g,m,x+padX,y+(String.IsNullOrEmpty(m.Title)?17:49),w-padX*2,view,Art.Muted);
        }
    }
}
