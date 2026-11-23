using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Polya {
    sealed partial class Workspace : Form {
        readonly Surface surface=new Surface();
        readonly List<HitTarget> hits=new List<HitTarget>();
        readonly ToolTip tips=new ToolTip{InitialDelay=600,ReshowDelay=150};
        readonly Timer recoveryTimer=new Timer{Interval=1800};
        readonly Timer zoomTimer=new Timer{Interval=15};
        readonly Timer renderTimer=new Timer{Interval=15};
        float targetZoom;
        PointF zoomAnchor;
        long zoomFrame;
        static readonly string RecoveryPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Polya","session.polya");
        Notebook doc;
        string path,status="Готово к работе";
        bool dirty,firstLayout=true,recoveryAllowed;
        Task recovery;
        int version;
        float scale=1,screenW,screenH;
        PointF pointer=new PointF(-1,-1);
        bool pointerSeen;
        string lastTip;
        readonly string[] swatches={"#5F43CD","#292339","#16836A","#2766AC","#BA3E60","#C56A1B"};
        sealed class HitTarget { public RectangleF Rect; public Action Action; public string Tip; }
        public Workspace(string openPath=null,bool allowRecovery=true) {
            recoveryAllowed=allowRecovery;
            Text="Поля";
            Font=new Font("Segoe UI",10);
            Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition=FormStartPosition.CenterScreen;
            var work=Screen.PrimaryScreen.WorkingArea;
            using(var display=CreateGraphics()) scale=Math.Max(1,display.DpiX/96f);
            MinimumSize=new Size(Math.Min((int)(965*scale),work.Width),Math.Min((int)(675*scale),work.Height));
            ClientSize=new Size(Math.Min((int)(1440*scale),work.Width-36),Math.Min((int)(865*scale),work.Height-65));
            KeyPreview=true; DoubleBuffered=true; ResizeRedraw=true;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            doc=Seed.Create(true);
            if(recoveryAllowed&&String.IsNullOrEmpty(openPath)&&File.Exists(RecoveryPath)) {
                try { doc=Notebook.Read(RecoveryPath); status="Локальная копия восстановлена"; firstLayout=false; }
                catch(Exception) { status="Не удалось восстановить локальную копию"; }
            }
            surface.Bind(doc.Boards[doc.Active]);
            surface.UseRaster();
            surface.Changed+=()=>{ dirty=true; RecordSession(); Invalidate(); };
            surface.EditMark+=EditMark;
            surface.PreviewLink+=Preview;
            surface.RevealTask+=ToggleTask;
            recoveryTimer.Tick+=AutoSave;
            zoomTimer.Tick+=(s,e)=>{
                float zoom=surface.Board.Zoom;
                double log=Math.Log(targetZoom/zoom);
                long now=Stopwatch.GetTimestamp();
                double elapsed=Math.Max(.012,Math.Min(.09,(now-zoomFrame)/(double)Stopwatch.Frequency));
                zoomFrame=now;
                if(Math.Abs(log)<.004) {surface.ZoomAt(targetZoom/zoom,zoomAnchor);StopZoom();RecordSession();}
                else surface.ZoomAt((float)Math.Exp(log*(1-Math.Exp(-elapsed/.045))),zoomAnchor);
                Dirty(surface.Area);
            };
            renderTimer.Tick+=(s,e)=>{
                if(page!=ScreenPage.Board) {renderTimer.Stop();return;}
                var completed=surface.PrepareTiles(5);
                var affected=RectangleF.Intersect(surface.Area,completed);
                if(affected.Width>0&&affected.Height>0)Dirty(affected);
                if(!surface.MoreTiles) renderTimer.Stop();
            };
            InitializeLesson();
            LoadRecent();
            if(!String.IsNullOrEmpty(openPath)) LoadNotebook(openPath);
        }
        void AutoSave(object sender,EventArgs e) {
            if(recovery!=null&&!recovery.IsCompleted) return;
            recoveryTimer.Stop();
            var snapshot=Snapshot();
            int writing=version;
            recovery=Task.Run(()=>{
                try { Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath)); snapshot.Write(RecoveryPath); }
                catch(Exception) {
                    if(!IsDisposed&&IsHandleCreated) try { BeginInvoke(new Action(()=>{ if(writing==version) Notice("Не удалось сохранить локальную копию"); })); }
                    catch(InvalidOperationException) {}
                }
            });
        }
        Notebook Snapshot() {
            return new Notebook {Version=doc.Version,Active=doc.Active,Boards=doc.Boards.Select(b=>new Board {
                Name=b.Name,Subject=b.Subject,Zoom=presenting&&b==surface.Board?beforeZoom:b.Zoom,
                ViewX=presenting&&b==surface.Board?beforeX:b.ViewX,ViewY=presenting&&b==surface.Board?beforeY:b.ViewY,
                Background=b.Background,Pattern=b.Pattern,Straighten=b.Straighten,Marks=new List<Mark>(b.Marks)
            }).ToList()};
        }
        void RecordSession() {
            if(!recoveryAllowed) return;
            version++;
            recoveryTimer.Stop(); recoveryTimer.Start();
        }
        protected override void OnFormClosing(FormClosingEventArgs e) {
            if(presenting)EndPresentation();
            if(!CheckUnsaved()) {e.Cancel=true;return;}
            if(recoveryAllowed&&recoveryTimer.Enabled) {
                recoveryTimer.Stop();
                try {
                    if(recovery!=null) recovery.Wait();
                    Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath));
                    Snapshot().Write(RecoveryPath);
                } catch(Exception ex) {
                    var choice=Prompts.Confirm(this,"Локальная копия недоступна","Не удалось сохранить локальную копию.\n"+ex.Message+"\n\nЗакрыть программу?","Закрыть");
                    if(choice!=DialogResult.Yes) {e.Cancel=true;RecordSession();return;}
                }
            }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing) {
            if(disposing) { surface.Cancel();surface.ReleaseRaster();recoveryTimer.Dispose();fanTimer.Dispose();zoomTimer.Dispose();renderTimer.Dispose();lessonTimer.Dispose();tips.Dispose(); }
            base.Dispose(disposing);
        }
        RectangleF R(float x,float y,float w,float h) { return new RectangleF(x,y,w,h); }
        void LayoutUi(Graphics g) {
            scale=Math.Max(1,g.DpiX/96f);
            surface.DeviceScale=scale;
            bool resize=screenW!=ClientSize.Width/scale||screenH!=ClientSize.Height/scale;
            screenW=ClientSize.Width/scale; screenH=ClientSize.Height/scale;
            surface.Area=R(0,0,Math.Max(1,screenW),Math.Max(1,screenH));
            if(presenting&&resize)FocusFrame(lessonFrames[lessonIndex]);
            if(firstLayout&&surface.Area.Width>100) {
                firstLayout=false;
                float initialZoom=Math.Min(1,Math.Min(surface.Area.Width/882f,surface.Area.Height/730f));
                foreach(var board in doc.Boards.Where(b=>b.Marks.Count>0)) {
                    board.Zoom=initialZoom;
                    board.ViewX=Math.Max(0,(surface.Area.Width-882*initialZoom)/2);
                    board.ViewY=32;
                }
            }
        }
        protected override void OnPaint(PaintEventArgs e) {
            LayoutUi(e.Graphics);
            var g=e.Graphics; g.SetClip(e.ClipRectangle);g.ScaleTransform(scale,scale); Art.Prepare(g);
            hits.Clear();
            if(page!=ScreenPage.Board) { DrawLibrary(g); return; }
            if(presenting) {
                Art.Fill(g,Art.C("#F4F1FA"),surface.Area);
                var state=g.Save();
                g.SetClip(PresentationBounds(),System.Drawing.Drawing2D.CombineMode.Intersect);
                surface.Draw(g);g.Restore(state);
                DrawPresentation(g);
            } else {surface.Draw(g);DrawBoardShell(g);}
            if(surface.MoreTiles&&!renderTimer.Enabled)renderTimer.Start();
        }
        bool Hover(RectangleF rect) { return pointerSeen&&rect.Contains(pointer); }
        void Target(RectangleF rect,Action run,string tip=null) { hits.Add(new HitTarget{Rect=rect,Action=run,Tip=tip}); }
        void Button(Graphics g,RectangleF rect,string icon,string label,Action run,string tip=null,bool primary=false,bool active=false,bool disabled=false) {
            Color fg=primary?Art.White:active?Art.Purple:disabled?Art.Fade:Art.Ink;
            var fill=primary?(Hover(rect)?Art.C("#5134BC"):Art.Purple):active?Art.Pale:Hover(rect)&&!disabled?Art.Sidebar:Art.White;
            Art.Round(g,rect,8,fill,primary||active?null:(Color?)Art.Line);
            if(label==null) Art.Icon(g,icon,rect.X+(rect.Width-19)/2,rect.Y+(rect.Height-19)/2,19,fg);
            else {
                float x=rect.X+12;
                if(icon!=null) { Art.Icon(g,icon,x,rect.Y+(rect.Height-18)/2,18,fg); x+=24; }
                Art.Label(g,label,x,rect.Y,rect.Right-x-7,rect.Height,13,fg,true,icon==null?StringAlignment.Center:StringAlignment.Near);
            }
            if(!disabled) Target(rect,run,tip);
        }
        static Color SubjectBg(string s) {
            switch(s) {case "math":return Art.Pale;case "physics":return Art.C("#DCF3EB");case "russian":return Art.C("#F9E6F0");default:return Art.C("#E6EDF5");}
        }
        static Color SubjectFg(string s) {
            switch(s) {case "math":return Art.Purple;case "physics":return Art.C("#217B64");case "russian":return Art.C("#A14871");default:return Art.C("#3D5F7D");}
        }
        void BottomButton(Graphics g,RectangleF r,string icon,string text,Action action,string tip) {
            if(Hover(r)) Art.Round(g,r,6,Art.Sidebar);
            if(icon!=null) Art.Icon(g,icon,r.X+(r.Width-17)/2,r.Y+7,17,Art.Muted);
            else Art.Label(g,text,r.X,r.Y,r.Width,r.Height,12,Art.Ink,true,StringAlignment.Center);
            Target(r,action,tip);
        }
        void Notice(string value) { status=value; Invalidate(); }
        void ViewChanged() { RecordSession(); Invalidate(); }
        void Zoom(float amount) {
            var area=surface.Area; AnimateZoom(amount,new PointF(area.Left+area.Width/2,area.Top+area.Height/2));
        }
        void AnimateZoom(float amount,PointF anchor) {
            targetZoom=Math.Max(.15f,Math.Min(4,(zoomTimer.Enabled?targetZoom:surface.Board.Zoom)*amount));
            zoomAnchor=anchor;
            if(!zoomTimer.Enabled)zoomFrame=Stopwatch.GetTimestamp();
            surface.AnimatingZoom=true;zoomTimer.Start();
        }
        void StopZoom() {zoomTimer.Stop();surface.AnimatingZoom=false;}
        void ChooseTool(string tool) {
            if(tool=="undo"||tool=="redo") surface.Undo(tool=="redo");
            else { popup=null; surface.Tool=tool; if(tool!="select") surface.ClearSelection(); }
            UpdateCursor(); Invalidate();
        }
        void ToggleTidy() {surface.Tidy=!surface.Tidy; MarkSettings();}
        void Dirty(RectangleF rect) {
            Invalidate(Rectangle.FromLTRB((int)Math.Floor(rect.Left*scale),(int)Math.Floor(rect.Top*scale),
                (int)Math.Ceiling(rect.Right*scale),(int)Math.Ceiling(rect.Bottom*scale)));
        }
        PointF Logical(Point p) { return new PointF(p.X/scale,p.Y/scale); }
        Point Pixel(PointF p) { return new Point((int)(p.X*scale),(int)(p.Y*scale)); }
        HitTarget At(PointF p) { for(int i=hits.Count-1;i>=0;i--) if(hits[i].Rect.Contains(p)) return hits[i]; return null; }
        void UpdateCursor() {
            var hit=At(pointer);
            bool onBoard=page==ScreenPage.Board&&!presenting&&surface.Area.Contains(pointer);
            if(hit!=null||onBoard&&(surface.Space||surface.Tool=="hand")) {Cursor=Cursors.Hand;return;}
            if(onBoard&&surface.Tool=="select"&&DiagramEdit.HandleAt(surface.Selected,surface.World(pointer),9/surface.Board.Zoom)>=0) {
                Cursor=Cursors.SizeAll;return;
            }
            Cursor=onBoard&&surface.Tool!="select"?Cursors.Cross:Cursors.Default;
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            var at=Logical(e.Location); var control=At(at);
            if(e.Button==MouseButtons.Left&&control!=null&&e.Clicks==1) {control.Action();Invalidate();return;}
            if(page!=ScreenPage.Board) return;
            if(presenting) return;
            StopZoom();
            if(popup!=null) {popup=null; Invalidate(); return;}
            if(!surface.Area.Contains(at)||control!=null&&e.Button==MouseButtons.Left) return;
            if(e.Button==MouseButtons.Right) {CanvasMenu(e.Location);return;}
            if(e.Button==MouseButtons.Left&&e.Clicks>1) surface.OnDouble(at);
            else if(e.Button==MouseButtons.Left||e.Button==MouseButtons.Middle) {
                surface.OnDown(at,e.Button==MouseButtons.Middle,(ModifierKeys&Keys.Shift)!=0);
                if(surface.Dragging) Capture=true;
            }
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            var previous=pointer; var old=At(previous);
            pointer=Logical(e.Location); pointerSeen=true; var target=At(pointer);
            if(surface.Dragging) {
                var changed=surface.Track(pointer,(ModifierKeys&Keys.Shift)!=0);
                if(!changed.IsEmpty)Dirty(changed);
            }
            if(lastTip!=(target==null?null:target.Tip)) {lastTip=target==null?null:target.Tip; tips.SetToolTip(this,lastTip??"");}
            UpdateCursor();
            if(old!=target) {
                if(old!=null) Dirty(RectangleF.Inflate(old.Rect,2,2));
                if(target!=null) Dirty(RectangleF.Inflate(target.Rect,2,2));
                if(fan) Dirty(R(screenW-270,screenH-345,270,345));
            }
            if(page==ScreenPage.Board&&surface.Tool=="eraser") {
                Dirty(R(previous.X-12,previous.Y-12,24,24));
                Dirty(R(pointer.X-12,pointer.Y-12,24,24));
            }
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            if(surface.Dragging) {
                surface.OnUp(Logical(e.Location),(ModifierKeys&Keys.Shift)!=0);
                Capture=false; UpdateCursor(); ViewChanged();
            }
        }
        protected override void OnMouseCaptureChanged(EventArgs e) {
            base.OnMouseCaptureChanged(e);
            if(!Capture&&surface.Dragging) {surface.Cancel(); Invalidate();}
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); pointerSeen=false; Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) {
            base.OnMouseWheel(e);
            var at=Logical(e.Location);
            if(page!=ScreenPage.Board) {ScrollLibrary(e.Delta);return;}
            if(presenting||surface.Dragging||!surface.Area.Contains(at)) return;
            AnimateZoom((float)Math.Pow(1.13,e.Delta/120.0),at);
        }
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if(presenting) {
                if(e.KeyCode==Keys.Escape) EndPresentation();
                else if(e.KeyCode==Keys.Right||e.KeyCode==Keys.Space||e.KeyCode==Keys.PageDown) StepLesson(1);
                else if(e.KeyCode==Keys.Left||e.KeyCode==Keys.PageUp) StepLesson(-1);
                else if(e.KeyCode==Keys.Enter) RevealOnSlide();
                e.SuppressKeyPress=true;return;
            }
            if(page!=ScreenPage.Board) {
                if(e.Control&&e.KeyCode==Keys.O) OpenNotebook();
                else if(e.KeyCode==Keys.Escape&&page==ScreenPage.Files) Navigate(ScreenPage.Subjects);
                return;
            }
            if(e.KeyCode==Keys.Escape&&(popup!=null||fan)) {popup=null;if(fan)ToggleFan();Invalidate();return;}
            if(e.Control) {
                switch(e.KeyCode) {
                    case Keys.Z: surface.Undo(e.Shift); break;
                    case Keys.Y: surface.Undo(true); break;
                    case Keys.S: Save(e.Shift); break;
                    case Keys.O: OpenNotebook(); break;
                    case Keys.N: NewNotebook(); break;
                    case Keys.D:
                        DuplicateSelected();break;
                    default:return;
                }
            } else switch(e.KeyCode) {
                case Keys.Space: surface.Space=true; UpdateCursor(); break;
                case Keys.Delete: if(!surface.Dragging) surface.Delete();break;
                case Keys.Escape: surface.Cancel(); surface.ClearSelection(); Capture=false; ChooseTool("select"); break;
                case Keys.V: ChooseTool("select");break; case Keys.H:ChooseTool("hand");break; case Keys.P:ChooseTool("pen");break;
                case Keys.R: ChooseTool("rect");break; case Keys.O:ChooseTool("ellipse");break; case Keys.A:ChooseTool("arrow");break;
                case Keys.I: case Keys.L: ChooseTool("line");break; case Keys.T:ChooseTool("text");break; case Keys.N:ChooseTool("note");break;
                case Keys.D: ChooseTool("diamond");break;
                case Keys.K: ChooseTool("link");break; case Keys.E:ChooseTool("eraser");break;
                case Keys.Q: surface.KeepTool=!surface.KeepTool;break;
                case Keys.D1: ChooseTool("select");break;
                case Keys.D2: ChooseTool("rect");break;
                case Keys.D3: ChooseTool("diamond");break;
                case Keys.D4: ChooseTool("ellipse");break;
                case Keys.D5: ChooseTool("arrow");break;
                case Keys.D6: ChooseTool("line");break;
                case Keys.D7: ChooseTool("pen");break;
                case Keys.D8: ChooseTool("text");break;
                case Keys.F1: Help();break; case Keys.F2: RenameBoard();break; case Keys.D0:surface.Fit();ViewChanged();break;
                case Keys.F5: StartPresentation();break;
                case Keys.Enter:
                    if(surface.Selected!=null) {
                        if(surface.Selected.Kind=="link") Preview(surface.Selected);
                        else if(surface.Selected.Kind=="task") ToggleTask(surface.Selected);
                        else EditMark(surface.Selected);
                    }
                    break;
                default:return;
            }
            e.SuppressKeyPress=true; Invalidate();
        }
        protected override void OnKeyUp(KeyEventArgs e) {base.OnKeyUp(e);if(e.KeyCode==Keys.Space) {surface.Space=false; UpdateCursor();}}
        protected override void OnDeactivate(EventArgs e) {base.OnDeactivate(e);surface.Space=false;}
        void SelectBoard(int index) {
            if(index==doc.Active) return;
            StopZoom();
            doc.Active=index; surface.Bind(doc.Boards[index]); ChooseTool("select"); ViewChanged();
        }
        void AddBoard() {
            string[] subjects=Seed.Subjects.Select(Seed.Subject).ToArray();
            string name; int subject;
            if(!Dialogs.BoardName(this,"Новая доска","",subjects,out name,out subject)) return;
            doc.Boards.Add(Board.Make(name,Seed.Subjects[subject]));
            SelectBoard(doc.Boards.Count-1);
            dirty=true; Invalidate();
        }
        void RenameBoard() {
            string name; int unused;
            if(Dialogs.BoardName(this,"Название доски",surface.Board.Name,null,out name,out unused)) {
                surface.Board.Name=name; dirty=true; ViewChanged();
            }
        }
        void DeleteBoard() {
            if(doc.Boards.Count<2) {Prompts.Alert(this,"Последняя доска","В тетради должна остаться хотя бы одна доска.");return;}
            if(Prompts.Confirm(this,"Удалить доску?","Доска «"+surface.Board.Name+"» и её объекты будут удалены из тетради.","Удалить")!=DialogResult.Yes) return;
            surface.Forget(surface.Board); doc.Boards.RemoveAt(doc.Active);
            doc.Active=Math.Max(0,doc.Active-1); surface.Bind(doc.Boards[doc.Active]);
            dirty=true; ViewChanged();
        }
        void EditMark(Mark mark) {
            if(mark==null) return;
            bool added=!surface.Board.Marks.Contains(mark);
            Mark next;
            switch(mark.Kind) {
                case "diagram":next=DiagramEdit.Properties(this,mark);break;
                case "task":next=EditTask(mark);break;
                case "frame":
                    string name;int unused;
                    if(!Dialogs.BoardName(this,"Название кадра",mark.Title,null,out name,out unused))return;
                    next=mark.Copy();next.Title=name;break;
                case "graph":next=Dialogs.Graph(this,mark);break;
                case "trig":next=Dialogs.Trig(this,mark);break;
                case "heading":case "text":case "note":case "link":next=Dialogs.Edit(this,mark);break;
                default:return;
            }
            if(next==null) return;
            if(next.Kind=="text"||next.Kind=="note") TextLayout.Fit(next);
            if(added) {surface.Add(next);if(!surface.KeepTool||mark.Kind=="graph"||mark.Kind=="trig") surface.Tool="select";}
            else surface.Replace(mark,next);
            Invalidate();
        }
        void NewWidget(string kind) {
            var center=surface.Center();
            var graph=kind=="graph";
            EditMark(Mark.Make(kind,center.X-215,center.Y-155,430,graph?310:346,graph?"График функции":"Тригонометрическая окружность",graph?"sin(x)":""));
        }
        void Calculator() { using(var calc=new CalculatorWindow()) calc.ShowDialog(this); }
        void Reference() {
            var reference=Dialogs.Reference(this,surface.Board.Subject);
            if(reference==null) return;
            var center=surface.Center();
            var m=Mark.Make("text",center.X-220,center.Y-85,440,150,reference[0],reference[1]); TextLayout.Fit(m); surface.Add(m); ChooseTool("select");
        }
        void Preview(Mark mark) { Dialogs.Preview(this,mark); }
        void Help() { Dialogs.Help(this); }
        void CanvasMenu(Point at) {
            contextAt=Logical(at);popup="canvas";Invalidate();
        }
        void Drive() {
            if(Dialogs.Drive(this)) Save(true);
        }
        bool CheckUnsaved() {
            if(!dirty) return true;
            var choice=Prompts.Changes(this);
            return choice==DialogResult.No||choice==DialogResult.Yes&&Save(false);
        }
        bool Save(bool copy) {
            string candidate=path;
            if(copy||String.IsNullOrEmpty(candidate)) {
                candidate=FileSheet.Pick(this,true,path==null?null:Path.GetDirectoryName(path),path==null?"Моя тетрадь.polya":Path.GetFileName(path));
                if(candidate==null) return false;
            }
            try {doc.Write(candidate);path=candidate;Remember(candidate);dirty=false;Notice("Сохранено: "+Path.GetFileName(path));return true;}
            catch(Exception ex) {Prompts.Alert(this,"Не удалось сохранить файл",ex.Message);return false;}
        }
        void OpenNotebook() {
            string selected=FileSheet.Pick(this,false,path==null?null:Path.GetDirectoryName(path),null);
            if(selected!=null&&CheckUnsaved()) LoadNotebook(selected);
        }
        void LoadNotebook(string source) {
            try { var candidate=Notebook.Read(source);
                if(page==ScreenPage.Files) {int wanted=candidate.Boards.FindIndex(b=>b.Subject==librarySubject); if(wanted>=0&&candidate.Boards[candidate.Active].Subject!=librarySubject) candidate.Active=wanted;}
                SetNotebook(candidate,source); Remember(source); }
            catch(Exception ex) { Prompts.Alert(this,"Не удалось открыть файл",ex.Message); }
        }
        void NewNotebook() {if(CheckUnsaved()) SetNotebook(Seed.Create(false),null);}
        void SetNotebook(Notebook next,string source) {
            doc=next; path=source; dirty=false; firstLayout=false; Navigate(ScreenPage.Board);
            surface.Reset(); surface.Bind(doc.Boards[doc.Active]); ChooseTool("select"); RecordSession();
            Notice(source==null?"Новая тетрадь":"Открыт файл: "+Path.GetFileName(source));
        }
    }
}
