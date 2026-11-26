using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Polya {
    static class LessonArt {
        sealed class CachedImage {
            public readonly Bitmap Bitmap;
            public CachedImage(Mark item) {
                try {
                    if(String.IsNullOrEmpty(item.Text)||item.Text.Length>16*1024*1024)return;
                    using(var bytes=new MemoryStream(Convert.FromBase64String(item.Text)))
                    using(var source=Image.FromStream(bytes,true,true)) {
                        if((long)source.Width*source.Height<=2560L*2560) {
                            Bitmap=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);
                            using(var g=Graphics.FromImage(Bitmap)) {
                                g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                                g.DrawImage(source,0,0,source.Width,source.Height);
                            }
                            if(item.RemoveWhite)ClearPaper(Bitmap);
                        }
                    }
                } catch(ArgumentException) {} catch(FormatException) {}
            }
        }
        static readonly ConditionalWeakTable<Mark,CachedImage> images=new ConditionalWeakTable<Mark,CachedImage>();
        static void ClearPaper(Bitmap image) {
            var rect=new Rectangle(0,0,image.Width,image.Height);
            var data=image.LockBits(rect,ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
            try {
                var row=new byte[image.Width*4];
                for(int y=0;y<image.Height;y++) {
                    var origin=IntPtr.Add(data.Scan0,y*data.Stride);
                    Marshal.Copy(origin,row,0,row.Length);
                    for(int p=0;p<row.Length;p+=4) {
                        int light=Math.Min(row[p],Math.Min(row[p+1],row[p+2]));
                        int high=Math.Max(row[p],Math.Max(row[p+1],row[p+2]));
                        if(light<231||high-light>18)continue;
                        row[p+3]=(byte)(row[p+3]*Math.Max(0,249-light)/18);
                    }
                    Marshal.Copy(row,0,origin,row.Length);
                }
            } finally {image.UnlockBits(data);}
        }
        public static Mark Worksheet(string path,PointF center) {
            if(new FileInfo(path).Length>24L*1024*1024) throw new FormatException("Выберите изображение размером до 24 МБ.");
            using(var file=File.OpenRead(path))return Worksheet(file,center,Path.GetFileNameWithoutExtension(path));
        }
        public static Mark Worksheet(Stream file,PointF center,string name) {
            using(var source=Image.FromStream(file,true,true)) {
                if((long)source.Width*source.Height>36000000) throw new FormatException("Изображение слишком большое. Уменьшите его до 36 мегапикселей.");
                double scale=Math.Min(1,2200.0/Math.Max(source.Width,source.Height));
                using(var resized=new Bitmap(Math.Max(1,(int)(source.Width*scale)),Math.Max(1,(int)(source.Height*scale)),PixelFormat.Format32bppPArgb))
                using(var g=Graphics.FromImage(resized))
                using(var bytes=new MemoryStream()) {
                    g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(source,0,0,resized.Width,resized.Height);
                    resized.Save(bytes,ImageFormat.Png);
                    if(bytes.Length>12*1024*1024) {bytes.SetLength(0);bytes.Position=0;resized.Save(bytes,ImageFormat.Jpeg);}
                    float boardScale=Math.Min(1,660f/Math.Max(resized.Width,resized.Height));
                    var w=resized.Width*boardScale;var h=resized.Height*boardScale;
                    return Mark.Make("image",center.X-w/2,center.Y-h/2,w,h,name,
                        Convert.ToBase64String(bytes.ToArray()));
                }
            }
        }
        public static void Draw(Graphics g,Mark mark) {
            float x=mark.X,y=mark.Y,w=mark.W,h=mark.H;
            if(mark.Kind=="image") {
                var image=images.GetValue(mark,m=>new CachedImage(m)).Bitmap;
                if(image!=null)g.DrawImage(image,mark.Bounds);
                else Art.Label(g,"Изображение недоступно",x,y,w,h,14,Art.Muted,false,StringAlignment.Center);
                return;
            }
            if(mark.Kind=="frame") {
                Art.Round(g,mark.Bounds,17,Art.C("#F5F2FD"),Art.C("#D9D2EF"));
                Art.Round(g,new RectangleF(x+13,y+11,w-26,36),10,Art.C("#E8E0FA"));
                Art.Icon(g,"frame",x+24,y+20,18,Art.Purple);
                Art.Label(g,mark.Title,x+51,y+11,w-78,36,16,Art.Purple,true);
                return;
            }
            Art.Round(g,new RectangleF(x,y+4,w,h),13,Color.FromArgb(16,32,20,72));
            Art.Round(g,mark.Bounds,13,Art.White,Art.C("#DDD4F3"));
            Art.Round(g,new RectangleF(x+16,y+14,33,33),9,Art.Pale);
            Art.Icon(g,"task",x+23,y+21,19,Art.Purple);
            Art.Label(g,mark.Title,x+60,y+13,w-80,35,17,Art.Ink,true);
            float promptHeight=(h-109)/2;
            Art.Paragraph(g,mark.Text,x+20,y+59,w-40,Math.Max(1,promptHeight),16,Art.Ink);
            var answer=new RectangleF(x+13,y+62+promptHeight,w-26,h-75-promptHeight);
            Art.Round(g,answer,10,mark.Revealed?Art.C("#E8F7EE"):Art.C("#F3EEFC"));
            Art.Icon(g,mark.Revealed?"check":"lock",answer.X+11,answer.Y+10,16,mark.Revealed?Art.C("#217B64"):Art.Purple);
            Art.Label(g,mark.Revealed?"Ответ":"Показать ответ · двойной щелчок",answer.X+35,answer.Y+6,answer.Width-46,25,13,Art.Purple,true);
            if(mark.Revealed) Art.Paragraph(g,mark.Answer,answer.X+13,answer.Y+33,answer.Width-26,answer.Height-39,15,Art.Ink);
        }
        public static Board Template(string name,string subject) {
            var board=Board.Make(name,subject);board.Pattern="none";board.Background="#F8F6FC";
            string objective,prompt,solution,practice,practiceAnswer,summary;
            switch(subject) {
                case "math":
                    objective="Сравнить графики y = x² и y = ax². Какие изменения видны, когда a отрицательно?";
                    prompt="Предскажи, как изменится парабола y = ax² при a = −2. Назови вершину и направление ветвей.";
                    solution="Вершина остаётся в (0; 0), ветви направлены вниз. При одинаковом |x| значение |y| вдвое больше, чем для y = x².";
                    practice="Для y = 3x² найди y при x = −2, −1, 0, 1 и 2. Отметь точки на координатной плоскости.";
                    practiceAnswer="12, 3, 0, 3, 12. График симметричен относительно оси Oy.";
                    summary="Знак a задаёт направление ветвей, модуль |a| влияет на форму параболы.";
                    break;
                case "physics":
                    objective="Объяснить роль массы и равнодействующей силы. Перевести величины в СИ перед подстановкой.";
                    prompt="Тележка массой 2 кг движется под действием равнодействующей 6 Н. Найди ускорение.";
                    solution="a = F/m = 6 Н / 2 кг = 3 м/с². Ускорение направлено вдоль равнодействующей.";
                    practice="Сила 15 Н разгоняет тело массой 5 кг. Как изменится ускорение при удвоении массы?";
                    practiceAnswer="Сначала 3 м/с², после удвоения массы 1,5 м/с².";
                    summary="При одинаковой силе ускорение обратно пропорционально массе. Равнодействующая учитывает все силы.";
                    break;
                case "russian":
                    objective="Находить грамматические основы и объяснять пунктуацию в сложносочинённом предложении.";
                    prompt="Поставь знак: «Солнце село и погасли окна». Подчеркни грамматические основы.";
                    solution="Солнце село, и погасли окна. Основы: солнце село и окна погасли.";
                    practice="Где нужна запятая: «Ветер стих и утихли волны» или «Ветер стих и похолодал»? Объясни.";
                    practiceAnswer="Запятая нужна в первом предложении, где две основы. Во втором один субъект и два однородных сказуемых.";
                    summary="Сначала найди грамматические основы. Затем проверь, какой союз соединяет части и есть ли общее обстоятельство.";
                    break;
                default:
                    objective="Сформулируйте цель урока и запишите, что ученики смогут сделать к его концу.";
                    prompt="Замените текст на вопрос для совместного разбора.";
                    solution="Поместите сюда короткое объяснение и пример решения.";
                    practice="Добавьте самостоятельное задание.";
                    practiceAnswer="Добавьте критерии проверки или ответ.";
                    summary="Что получилось? Какой вопрос осталось разобрать?";
                    break;
            }
            Frame(board,50,54,"Разминка",Mark.Make("text",89,128,600,114,"Цель урока",objective),Task(89,268,690,210,"Вопрос для обсуждения",prompt,solution));
            Mark illustration=subject=="math"?Mark.Make("graph",1049,132,477,325,"Парабола с параметром","a*x^2"):
                subject=="physics"?Mark.Make("diagram",1049,132,477,325,"Силы на наклонной","forces"):
                subject=="russian"?Mark.Make("diagram",1049,132,477,325,"Схема предложения","compound"):
                Mark.Make("note",1049,132,400,173,"Материалы","Вставьте в этот кадр рисунок, формулу или ссылку.","#EEE7FF");
            if(subject=="math") illustration.Parameter=1;
            Frame(board,1010,54,"Объяснение",illustration,Mark.Make("text",1551,132,310,240,"Главная мысль",summary));
            Frame(board,1970,54,"Практика",Task(2009,134,730,238,"Самостоятельное задание",practice,practiceAnswer),
                Mark.Make("note",2009,405,535,103,"Итог урока","Запишите главное правило своими словами.","#FFF1C3"));
            return board;
        }
        static void Frame(Board board,float x,float y,string name,params Mark[] objects) {
            board.Marks.Add(Mark.Make("frame",x,y,915,520,name));
            foreach(var item in objects) {
                if(item.Kind=="text"||item.Kind=="note") TextLayout.Fit(item);
                board.Marks.Add(item);
            }
        }
        public static Mark Task(float x,float y,float w,float h,string title,string text,string answer) {
            var item=Mark.Make("task",x,y,w,h,title,text);item.Answer=answer;return item;
        }
    }
    sealed partial class Workspace {
        readonly Timer lessonTimer=new Timer{Interval=160};
        readonly Stopwatch countdown=new Stopwatch();
        TimeSpan timerLeft;
        bool timerActive,timerAlert,presenting,virtualSlide;
        int lessonIndex;
        Mark[] lessonFrames;
        float beforeX,beforeY,beforeZoom;
        double Remaining {get {return Math.Max(0,(timerLeft-countdown.Elapsed).TotalSeconds);}}
        void InitializeLesson() {
            lessonTimer.Tick+=(s,e)=>{
                if(countdown.IsRunning&&Remaining<=0) {countdown.Reset();timerLeft=TimeSpan.Zero;timerAlert=true;lessonTimer.Stop();}
                Dirty(R(screenW-260,presenting?12:67,250,52));
            };
        }
        void DrawLessonHeader(Graphics g) {
            float x=screenW-440;
            Button(g,R(x,14,108,35),"frame","Урок",()=>TogglePopup("lesson"),"Кадры, задания и таймер",active:popup=="lesson");
        }
        void DrawLessonPopup(Graphics g) {
            var r=R(screenW-309,60,295,263);
            PopupBase(g,r,"Урок");
            PopupRow(g,R(r.X+7,r.Y+39,281,34),"book","Шаблон урока",CreateLesson);
            PopupRow(g,R(r.X+7,r.Y+75,281,34),"frame","Добавить кадр",AddFrame);
            PopupRow(g,R(r.X+7,r.Y+111,281,34),"task","Задание с ответом",AddTask);
            PopupRow(g,R(r.X+7,r.Y+147,281,34),"timer","Таймер задания",EditTimer);
            PopupRow(g,R(r.X+7,r.Y+183,281,34),"other","Список кадров",PickFrame);
            PopupRow(g,R(r.X+7,r.Y+219,281,34),"play","Начать показ · F5",StartPresentation);
        }
        void CreateLesson() {
            string title;int subject;string[] names=Seed.Subjects.Select(Seed.Subject).ToArray();
            if(!Dialogs.BoardName(this,"Новый урок","Урок · "+Seed.Subject(surface.Board.Subject),names,out title,out subject,
                Array.IndexOf(Seed.Subjects,surface.Board.Subject)))return;
            var board=LessonArt.Template(title,Seed.Subjects[subject]);
            doc.Boards.Add(board);SelectBoard(doc.Boards.Count-1);
            FocusFrame(board.Marks[0]); MarkSettings();
        }
        void AddFrame() {
            string title;int unused;
            if(!Dialogs.BoardName(this,"Название кадра","Кадр "+(Frames().Length+1),null,out title,out unused))return;
            var c=surface.Center();
            surface.Add(Mark.Make("frame",c.X-457,c.Y-245,915,520,title));
            ChooseTool("select");
        }
        void AddTask() {
            var c=surface.Center();
            var item=LessonArt.Task(c.X-245,c.Y-125,490,250,"Задание","","");
            EditTask(item,edited=>{if(edited!=null){surface.Add(edited);ChooseTool("select");}});
        }
        void AddWorksheet() {
            var file=FileSheet.PickImage(this);
            if(file==null)return;
            try {surface.Add(LessonArt.Worksheet(file,surface.Center()));ChooseTool("select");}
            catch(Exception ex) {Prompts.Alert(this,"Изображение не открывается",ex.Message);}
        }
        void EditTask(Mark item,Action<Mark> done) {
            var next=item.Copy();
            var sheet=new Sheet("Задание","Ученик увидит ответ после раскрытия",546,529);
            sheet.Caption("Заголовок",22,96,502);var title=sheet.Field(item.Title,22,121,502);
            sheet.Caption("Условие",22,153,502);var question=sheet.Body(item.Text,22,178,502,121);
            sheet.Caption("Ответ или подсказка",22,306,502);var answer=sheet.Body(item.Answer,22,332,502,124);
            sheet.Footer("На доску",()=>{
                if(String.IsNullOrWhiteSpace(question.Text)||String.IsNullOrWhiteSpace(answer.Text)) return sheet.Error("Введите условие и ответ.");
                next.Title=String.IsNullOrWhiteSpace(title.Text)?"Задание":title.Text.Trim();
                next.Text=question.Text.Trim();next.Answer=answer.Text.Trim();
                var measure=Mark.Make("text",0,0,next.W,100,"",next.Text);TextLayout.Fit(measure);
                var prompt=measure.H;measure=Mark.Make("text",0,0,next.W,100,"",next.Answer);TextLayout.Fit(measure);
                next.H=Math.Max(250,Math.Max(prompt,measure.H)*2+85);
                return true;
            });
            sheet.OnBoard(this,result=>done(result==DialogResult.OK?next:null));
        }
        void ToggleTask(Mark item) {
            var updated=item.Copy();updated.Revealed=!item.Revealed;surface.Replace(item,updated);
        }
        Mark[] Frames() {return surface.Board.Marks.Where(m=>m.Kind=="frame").ToArray();}
        void PickFrame() {
            var frames=Frames();
            if(frames.Length==0){Prompts.Alert(this,"Кадров пока нет","Добавьте кадр или создайте шаблон урока.");return;}
            var sheet=new Sheet("Кадры урока","Выберите, какую часть доски показать",470,392);
            var list=sheet.Put(new ListBox{BorderStyle=BorderStyle.None,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=37,IntegralHeight=false},22,101,426,216);
            list.Items.AddRange(frames.Select(m=>m.Title).ToArray());
            list.DrawItem+=(s,e)=>{
                if(e.Index<0)return;
                Art.Prepare(e.Graphics);var r=new RectangleF(e.Bounds.X+1,e.Bounds.Y+2,e.Bounds.Width-3,e.Bounds.Height-4);
                bool selected=(e.State&DrawItemState.Selected)!=0;
                Art.Round(e.Graphics,r,8,selected?Art.Pale:Art.C("#F7F6FA"));
                Art.Icon(e.Graphics,"frame",r.X+8,r.Y+7,19,Art.Purple);
                Art.Label(e.Graphics,frames[e.Index].Title,r.X+34,r.Y,r.Width-44,r.Height,14,Art.Ink,selected);
            };
            list.SelectedIndex=0;list.DoubleClick+=(s,e)=>sheet.AcceptButton.PerformClick();
            sheet.Footer("Показать",()=>list.SelectedIndex>=0);
            sheet.OnBoard(this,result=>{if(result==DialogResult.OK){FocusFrame(frames[list.SelectedIndex]);ViewChanged();}});
        }
        void FocusFrame(Mark frame) {
            StopZoom();
            surface.Focus(frame.Bounds, presenting?54:100,presenting?100:146);
        }
        void StartPresentation() {
            CloseCanvasPanel();CloseBrowser();
            if(presenting)return;
            lessonFrames=Frames();
            virtualSlide=lessonFrames.Length==0;
            if(virtualSlide) {
                if(surface.Board.Marks.Count==0) {Prompts.Alert(this,"Пустая доска","Добавьте материалы на доску или создайте урок из шаблона.");return;}
                var r=surface.Board.Marks.Select(m=>m.Bounds).Aggregate(RectangleF.Union);
                lessonFrames=new[]{Mark.Make("frame",r.X,r.Y,r.Width,r.Height,surface.Board.Name)};
            }
            beforeX=surface.Board.ViewX;beforeY=surface.Board.ViewY;beforeZoom=surface.Board.Zoom;
            surface.Cancel();surface.ClearSelection();
            presenting=true;lessonIndex=0;popup=null;surface.Area=R(0,0,screenW,screenH);
            FocusFrame(lessonFrames[0]);Invalidate();
        }
        void EndPresentation() {
            presenting=false;lessonFrames=null;
            surface.Board.ViewX=beforeX;surface.Board.ViewY=beforeY;surface.Board.Zoom=beforeZoom;
            RecordSession();Invalidate();
        }
        void StepLesson(int direction) {
            int next=Math.Max(0,Math.Min(lessonFrames.Length-1,lessonIndex+direction));
            if(next==lessonIndex)return;
            lessonIndex=next;FocusFrame(lessonFrames[next]);Invalidate();
        }
        void RevealOnSlide() {
            var bounds=lessonFrames[lessonIndex].Bounds;
            var task=surface.Board.Marks.FirstOrDefault(m=>m.Kind=="task"&&bounds.Contains(m.Bounds)&&!m.Revealed);
            if(task!=null)ToggleTask(task);
        }
        RectangleF PresentationBounds() {
            var frame=lessonFrames[lessonIndex];var origin=surface.World(PointF.Empty);float z=surface.Board.Zoom;
            var r=R((frame.X-origin.X)*z,(frame.Y-origin.Y)*z,frame.W*z,frame.H*z);
            r.Inflate(virtualSlide?25:5,virtualSlide?25:5);
            return r;
        }
        void DrawPresentation(Graphics g) {
            var baseX=screenW/2-189;float y=screenH-56;
            var title=lessonFrames[lessonIndex].Title;
            Art.Round(g,R(baseX,y,378,42),12,Art.White,Art.Line);
            RailIcon(g,R(baseX+6,y+4,37,34),"back","Предыдущий кадр",()=>StepLesson(-1),false,lessonIndex==0);
            RailIcon(g,R(baseX+46,y+4,37,34),"chevron","Следующий кадр",()=>StepLesson(1),false,lessonIndex+1==lessonFrames.Length);
            Art.Label(g,(lessonIndex+1)+" / "+lessonFrames.Length+"  ·  "+title,baseX+91,y,197,42,13,Art.Ink,true);
            RailIcon(g,R(baseX+295,y+4,37,34),"task","Раскрыть ответ · Enter",RevealOnSlide,false);
            RailIcon(g,R(baseX+335,y+4,37,34),"close","Завершить показ · Esc",EndPresentation,false);
            if(!virtualSlide) {
                var mark=lessonFrames[lessonIndex];var view=surface.World(PointF.Empty);
                float z=surface.Board.Zoom;
                var task=surface.Board.Marks.FirstOrDefault(m=>m.Kind=="task"&&mark.Bounds.Contains(m.Bounds)&&!m.Revealed);
                if(task!=null) Target(R((task.X-view.X)*z,(task.Y-view.Y+task.H*.62f)*z,task.W*z,task.H*.37f*z),()=>ToggleTask(task),"Показать ответ");
            }
            var graph=surface.Board.Marks.FirstOrDefault(m=>m.Kind=="graph"&&lessonFrames[lessonIndex].Bounds.Contains(m.Bounds)&&GraphArt.UsesParameter(m.Text));
            if(graph!=null) {
                var f=R(18,y,189,42);double value=graph.Parameter??1;
                Art.Round(g,f,12,Art.White,Art.Line);
                RailIcon(g,R(f.X+3,y+4,37,34),"minus","Уменьшить параметр a",()=>SetParameter(graph,value-.5),false,value<=-10);
                Art.Label(g,"a = "+value.ToString("0.##",CultureInfo.CurrentCulture),f.X+40,y,109,42,14,Art.Purple,true,StringAlignment.Center);
                RailIcon(g,R(f.X+150,y+4,37,34),"plus","Увеличить параметр a",()=>SetParameter(graph,value+.5),false,value>=10);
            }
            DrawLessonClock(g);
        }
        void SetParameter(Mark graph,double value) {
            var next=graph.Copy();next.Parameter=Math.Max(-10,Math.Min(10,value));surface.Replace(graph,next);
        }
        void EditTimer() {
            var sheet=new Sheet("Таймер урока","Введите длительность или выберите готовое время",470,286);
            sheet.Caption("Минуты",22,96,400);
            var time=sheet.Field("5",22,120,426);
            int[] presets={1,3,5,10};
            for(int i=0;i<presets.Length;i++) {
                int min=presets[i];
                sheet.Action(min+" мин",22+i*108,163,100,()=>time.Text=min.ToString());
            }
            sheet.Footer("Старт",()=>{
                double minutes;
                if(!Double.TryParse(time.Text.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out minutes)||minutes<1.0/60||minutes>180)
                    return sheet.Error("Введите время от одной секунды до 180 минут.");
                timerLeft=TimeSpan.FromMinutes(minutes);countdown.Restart();timerActive=true;timerAlert=false;lessonTimer.Start();
                return true;
            });
            sheet.OnBoard(this,result=>Invalidate());
        }
        void DrawLessonClock(Graphics g) {
            if(!timerActive)return;
            float x=screenW-245,y=presenting?15:68;
            var r=R(x,y,226,43);
            Art.Round(g,r,12,timerAlert?Art.C("#FDE9EE"):Art.White,Art.Line);
            var seconds=(int)Math.Ceiling(Remaining);
            Art.Icon(g,"timer",x+11,y+11,20,Art.Purple);
            Art.Label(g,timerAlert?"Время вышло":(seconds/60).ToString("00")+":"+(seconds%60).ToString("00"),x+36,y,106,43,16,Art.Purple,true);
            if(!timerAlert) RailIcon(g,R(x+147,y+4,34,34),countdown.IsRunning?"pause":"play","Пауза или продолжение",()=>{
                if(countdown.IsRunning)countdown.Stop();else countdown.Start();
            },false);
            RailIcon(g,R(x+182,y+4,34,34),"close","Закрыть таймер",()=>{timerActive=false;countdown.Reset();lessonTimer.Stop();},false);
        }
    }
}
