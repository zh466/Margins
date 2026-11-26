using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Polya {
    sealed partial class Workspace {
        Control canvasPanel;
        RichTextBox textInput;
        Mark textMark;
        float canvasPanelWidth;

        internal void ShowCanvasPanel(Control panel,float width) {
            FinishText(true);
            CloseCanvasPanel();
            StopZoom();
            popup=null;
            canvasPanel=panel;canvasPanelWidth=width;
            Controls.Add(panel);
            PositionCanvasPanel();panel.BringToFront();panel.Show();
            Invalidate();
        }
        void PositionCanvasPanel() {
            if(canvasPanel!=null) {
                int gap=(int)(12*scale),top=(int)(72*scale);
                int width=Math.Min((int)(canvasPanelWidth*scale),Math.Max(1,ClientSize.Width-gap*2));
                var bounds=new Rectangle(ClientSize.Width-width-gap,top,width,Math.Max(1,ClientSize.Height-top-gap));
                if(canvasPanel.Bounds!=bounds)canvasPanel.Bounds=bounds;
            }
            PositionBrowser();
        }
        void PositionBrowser() {
            if(browserHost==null||resizingBrowser)return;
            if(popup!=null){if(browserHost.Visible)browserHost.Hide();AlignWebInk();return;}
            var bounds=browserMark.PinnedWeb?BrowserBounds():browserFloatingBounds;
            if(bounds.IsEmpty) {
                int width=(int)(Math.Min(620,screenW*.55f)*scale);
                bounds=new Rectangle(ClientSize.Width-width-12,72,width,Math.Max(180,ClientSize.Height-84));
            }
            if(canvasPanel!=null&&bounds.IntersectsWith(canvasPanel.Bounds)) {
                int available=canvasPanel.Left-24;
                if(available>=bounds.Width)bounds.X=Math.Min(bounds.X,available-bounds.Width);
                else {
                    browserHost.Hide();AlignWebInk();return;
                }
            }
            if(browserHost.Bounds!=bounds)browserHost.Bounds=bounds;
            float factor=BrowserContentScale();
            if(browserChrome!=null)browserChrome.Apply(factor*scale,scale,factor*(browserMark.BrowserPageZoom??1),browserMark.PinnedWeb);
            if(!browserHost.Visible)browserHost.Show();
            if(canvasPanel!=null)canvasPanel.BringToFront();
            AlignWebInk();
        }
        internal void DetachCanvasPanel(Control panel) {
            if(panel==null||canvasPanel!=panel)return;
            canvasPanel=null;Controls.Remove(panel);PositionBrowser();Invalidate();
        }
        internal void CloseCanvasPanel() {
            var panel=canvasPanel;
            if(panel==null)return;
            DetachCanvasPanel(panel);panel.Dispose();
        }
        void CloseBrowser() {
            var panel=browserHost;
            if(panel==null)return;
            browserHost=null;browserMark=null;resizingBrowser=false;browserFloatingBounds=Rectangle.Empty;
            surface.ActiveBrowserMark=null;
            if(browserInk!=null){browserInk.Dispose();browserInk=null;}
            var webView=panel.Controls.OfType<WebView2>().FirstOrDefault();
            if(webView!=null) {
                try {webView.Dispose();}
                catch(InvalidCastException) {panel.Controls.Remove(webView);}
            }
            panel.Dispose();
            if(browserChrome!=null){browserChrome.Dispose();browserChrome=null;}
            Invalidate();
        }

        void EditTextOnCanvas(Mark mark) {
            FinishText(true);
            var area=surface.ScreenRect(mark.Bounds);
            int left=Math.Max(4,Math.Min(ClientSize.Width-165,(int)((area.Left+18*surface.Board.Zoom)*scale)));
            int top=Math.Max(65,(int)((area.Top+13*surface.Board.Zoom)*scale));
            top=Math.Min(ClientSize.Height-76,top);
            textMark=mark;
            textInput=new RichTextBox {
                BorderStyle=BorderStyle.None,DetectUrls=false,MaxLength=Int32.MaxValue,WordWrap=true,
                Font=new Font("Segoe UI",Math.Max(8,12*surface.Board.Zoom*scale),GraphicsUnit.Point),
                ForeColor=Art.Ink,BackColor=Art.ColorOf(surface.Board.Background,Art.White),
                Bounds=new Rectangle(left,top,Math.Max(100,Math.Min(ClientSize.Width-left-8,(int)((mark.W-42)*surface.Board.Zoom*scale))),
                    Math.Max(68,Math.Min(ClientSize.Height-top-8,(int)(Math.Max(125,area.Height)*scale)))),
                Text=mark.Text??""
            };
            textInput.KeyDown+=(s,e)=>{
                if(e.KeyCode==Keys.Escape||e.Control&&e.KeyCode==Keys.Enter) {
                    e.SuppressKeyPress=true;
                    FinishText(e.KeyCode!=Keys.Escape);
                }
            };
            textInput.TextChanged+=(s,e)=>RecordSession();
            textInput.LostFocus+=(s,e)=>FinishText(true);
            Controls.Add(textInput);textInput.BringToFront();textInput.Focus();textInput.SelectionStart=textInput.TextLength;
            surface.TextBeingEdited=mark;Invalidate();
        }
        void FinishText(bool save) {
            var editor=textInput;
            if(editor==null)return;
            var mark=textMark;
            var content=editor.Text;
            var font=editor.Font;
            textInput=null;textMark=null;surface.TextBeingEdited=null;
            editor.Dispose();font.Dispose();
            if(!save)RecordSession();
            if(save&&(!String.IsNullOrWhiteSpace(content)||!String.IsNullOrWhiteSpace(mark.Title))&&mark.Text!=content) {
                var next=mark.Copy();next.Text=content;
                CommitEditedMark(mark,next);
            }
            Invalidate();
        }

        async void OpenWebPage(Mark mark) {
            Uri address;
            if(!Dialogs.Url(mark.Text,out address))return;
            var holder=new Panel{BackColor=Art.White};
            var ink=new LiveWebInk(this);
            var header=new FlowLayoutPanel{Dock=DockStyle.Top,Height=(int)(55*scale),WrapContents=false,AutoScroll=true,BackColor=Art.Sidebar};
            var browser=new WebView2{Dock=DockStyle.Fill};
            var loading=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Text="Загрузка страницы…"};
            var close=new SheetButton("×",false){Width=39,Height=35,AccessibleName="Закрыть страницу"};
            var back=new SheetButton("Назад",false){Width=76,Height=35};
            var capture=new SheetButton("На доску",false){Width=91,Height=35,Enabled=false};
            var pin=new SheetButton(mark.PinnedWeb?"Открепить":"Закрепить",false){Width=91,Height=35};
            var annotate=new SheetButton("Рисовать",false){Width=86,Height=35,Enabled=false};
            var undoInk=new SheetButton("↶",false){Width=31,Height=35,Enabled=false,AccessibleName="Отменить линию на странице"};
            bool hasDocument=false;
            var move=new Label{Text="⠿",Width=29,Height=35,TextAlign=ContentAlignment.MiddleCenter,Cursor=Cursors.SizeAll,
                AccessibleName="Переместить браузер"};
            tips.SetToolTip(move,"Перетащить браузер");
            header.Controls.AddRange(new Control[]{close,move,back,pin,annotate,undoInk,capture});
            AddBrowserMoveHandle(move,holder,mark,browser);
            pin.Click+=(s,e)=>{
                if(browserMark.PinnedWeb) {
                    StoreBrowser(mark,m=>{m.PinnedWeb=false;m.W=285;m.H=94;});
                    browserFloatingBounds=holder.Bounds;
                    PositionCanvasPanel();
                } else PinBrowser(mark,holder.Bounds,browser.Source??address);
                pin.Text=browserMark.PinnedWeb?"Открепить":"Закрепить";
            };
            string currentPage=address.AbsoluteUri;
            Action loadPageInk=()=>{
                Mark[] strokes=null;
                float[][] legacy=null;
                if(browserMark.WebStrokes!=null)browserMark.WebStrokes.TryGetValue(currentPage,out strokes);
                if(strokes==null&&browserMark.PageAnnotations!=null)browserMark.PageAnnotations.TryGetValue(currentPage,out legacy);
                if(strokes==null&&legacy==null&&currentPage==mark.Text)legacy=mark.WebInk;
                AlignWebInk();ink.LoadStrokes(strokes,legacy);AlignWebInk();
                undoInk.Enabled=ink.CanUndo;
            };
            ink.Commit+=strokes=>{
                if(!browserMark.PinnedWeb||!surface.Board.Marks.Contains(browserMark))
                    PinBrowser(browserMark,holder.Bounds,new Uri(currentPage));
                StoreBrowser(browserMark,m=>{
                    var all=m.WebStrokes==null?new System.Collections.Generic.Dictionary<string,Mark[]>():
                        new System.Collections.Generic.Dictionary<string,Mark[]>(m.WebStrokes);
                    all[currentPage]=strokes;m.WebStrokes=all;
                });
                undoInk.Enabled=ink.CanUndo;
                pin.Text="Открепить";
            };
            undoInk.Click+=(s,e)=>ink.Undo();
            annotate.Click+=(s,e)=>{
                ink.SetMode(!ink.Drawing);
                annotate.Text=ink.Drawing?"Указатель":"Рисовать";
                AlignWebInk();
            };
            close.Click+=(s,e)=>CloseBrowser();
            back.Click+=(s,e)=>{if(browser.CanGoBack)browser.GoBack();};
            capture.Click+=async (s,e)=>{
                capture.Enabled=false;
                try {
                    using(var bytes=new MemoryStream()) {
                        await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,bytes);
                        if(holder.IsDisposed||bytes.Length==0)return;
                        bytes.Position=0;
                        var screenshot=bytes.ToArray();
                        holder.BeginInvoke(new Action(()=>{
                            if(holder.IsDisposed)return;
                            using(var raw=new MemoryStream(screenshot))
                            using(var source=Image.FromStream(raw))
                            using(var bitmap=new Bitmap(source))
                            using(var copy=new MemoryStream()) {
                                using(var g=Graphics.FromImage(bitmap))ink.DrawOn(g,bitmap.Width,bitmap.Height);
                                bitmap.Save(copy,System.Drawing.Imaging.ImageFormat.Png);copy.Position=0;
                                surface.Add(LessonArt.Worksheet(copy,surface.Center(),browser.Source.Host));
                            }
                        }));
                    }
                } catch(Exception) {if(!holder.IsDisposed)Notice("Не удалось перенести снимок страницы.");}
                finally {if(!holder.IsDisposed)capture.Enabled=true;}
            };
            holder.Controls.Add(browser);holder.Controls.Add(loading);holder.Controls.Add(header);
            FinishText(true);
            CloseCanvasPanel();CloseBrowser();StopZoom();popup=null;
            browserHost=holder;browserMark=mark;browserInk=ink;surface.ActiveBrowserMark=mark;
            browserChrome=new BrowserChrome(holder,header,browser,move);
            Controls.Add(holder);holder.BringToFront();
            AddBrowserGrip(holder,mark,browser);PositionBrowser();Invalidate();
            try {
                var profile=recoveryAllowed?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Polya","WebView2"):
                    Path.Combine(Path.GetTempPath(),"Margins-WebView2-QA");
                var environment=await CoreWebView2Environment.CreateAsync(null,profile);
                if(holder.IsDisposed)return;
                await browser.EnsureCoreWebView2Async(environment);
                if(holder.IsDisposed)return;
                var web=browser.CoreWebView2;
                web.Settings.AreHostObjectsAllowed=false;
                web.Settings.IsWebMessageEnabled=false;
                web.Settings.AreDefaultScriptDialogsEnabled=false;
                web.PermissionRequested+=(s,e)=>e.State=CoreWebView2PermissionState.Deny;
                web.DownloadStarting+=(sender,args)=>{args.Handled=true;args.Cancel=true;};
                web.NewWindowRequested+=(s,e)=>e.Handled=true;
                web.NavigationStarting+=(s,e)=>{Uri target;e.Cancel=!Dialogs.Url(e.Uri,out target);};
                web.SourceChanged+=(sender,args)=>{
                    if(holder.IsDisposed||browserHost!=holder)return;
                    Uri url;
                    if(Dialogs.Url(web.Source,out url)) {
                        currentPage=url.AbsoluteUri;
                        loadPageInk();
                    }
                };
                web.NavigationCompleted+=(s,e)=>{
                    if(holder.IsDisposed||browserHost!=holder)return;
                    if(!e.IsSuccess&&(hasDocument||e.WebErrorStatus==CoreWebView2WebErrorStatus.OperationCanceled))return;
                    hasDocument=e.IsSuccess;
                    capture.Enabled=annotate.Enabled=e.IsSuccess;
                    if(e.IsSuccess) {loading.Visible=false;browser.Visible=true;loadPageInk();}
                    else {browser.Visible=false;loading.Visible=true;loading.Text="Не удалось открыть страницу. Проверьте адрес и подключение.";}
                };
                PositionBrowser();
                web.Navigate(address.AbsoluteUri);
            } catch(Exception) {
                if(!holder.IsDisposed)loading.Text="Встроенный просмотр недоступен. Проверьте установку Microsoft Edge WebView2 Runtime.";
            }
        }
    }
}
