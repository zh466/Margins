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
        bool lastErasure;

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
            if(canvasPanel==null)return;
            int gap=(int)(12*scale),top=(int)(72*scale);
            int width=Math.Min((int)(canvasPanelWidth*scale),Math.Max(1,ClientSize.Width-gap*2));
            int height=Math.Max(1,ClientSize.Height-top-gap);
            var bounds=new Rectangle(ClientSize.Width-width-gap,top,width,height);
            if(canvasPanel.Bounds!=bounds)canvasPanel.Bounds=bounds;
        }
        internal void DetachCanvasPanel(Control panel) {
            if(panel==null||canvasPanel!=panel)return;
            canvasPanel=null;Controls.Remove(panel);Invalidate();
        }
        internal void CloseCanvasPanel() {
            var panel=canvasPanel;
            if(panel==null)return;
            canvasPanel=null;
            var webView=panel.Controls.OfType<WebView2>().FirstOrDefault();
            if(webView!=null) {
                try {webView.Dispose();}
                catch(InvalidCastException) {
                    // The WebView controller may already be disconnected when its window closes.
                    panel.Controls.Remove(webView);
                }
            }
            panel.Dispose();
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
            if(save&&(!String.IsNullOrWhiteSpace(content)||!String.IsNullOrWhiteSpace(mark.Title))&&mark.Text!=content) {
                var next=mark.Copy();next.Text=content;
                CommitEditedMark(mark,next);
            }
            Invalidate();
        }

        async void OpenWebPage(Mark mark) {
            Uri address;
            if(!Dialogs.Url(mark.Text,out address))return;
            var holder=new Panel{BackColor=Art.White,BorderStyle=BorderStyle.FixedSingle};
            var header=new FlowLayoutPanel{Dock=DockStyle.Top,Height=(int)(46*scale),WrapContents=false,BackColor=Art.Sidebar};
            var browser=new WebView2{Dock=DockStyle.Fill};
            var loading=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Text="Загрузка страницы…"};
            var close=new SheetButton("×",false){Width=39,Height=35,AccessibleName="Закрыть страницу"};
            var back=new SheetButton("Назад",false){Width=76,Height=35};
            var capture=new SheetButton("На доску",false){Width=91,Height=35,Enabled=false};
            var external=new SheetButton("В браузере",false){Width=108,Height=35};
            header.Controls.AddRange(new Control[]{close,back,capture,external,new Label{Text=address.Host,AutoSize=true,Padding=new Padding(5,9,0,0)}});
            close.Click+=(s,e)=>CloseCanvasPanel();
            back.Click+=(s,e)=>{if(browser.CanGoBack)browser.GoBack();};
            external.Click+=(s,e)=>Dialogs.OpenUrl(this,(browser.Source??address).AbsoluteUri);
            capture.Click+=async (s,e)=>{
                capture.Enabled=false;
                try {
                    using(var bytes=new MemoryStream()) {
                        await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,bytes);
                        if(holder.IsDisposed||bytes.Length==0)return;
                        bytes.Position=0;
                        var image=LessonArt.Worksheet(bytes,surface.Center(),browser.Source.Host);
                        surface.Add(image);
                        ChooseTool("pen");
                        CloseCanvasPanel();
                    }
                } catch(Exception) {if(!holder.IsDisposed)Notice("Не удалось перенести снимок страницы.");}
                finally {if(!holder.IsDisposed)capture.Enabled=true;}
            };
            holder.Controls.Add(browser);holder.Controls.Add(loading);holder.Controls.Add(header);
            ShowCanvasPanel(holder,Math.Min(620,screenW*.55f));
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
                web.DownloadStarting+=(s,e)=>{e.Cancel=true;e.Handled=true;};
                web.NewWindowRequested+=(s,e)=>e.Handled=true;
                web.NavigationStarting+=(s,e)=>{Uri target;e.Cancel=!Dialogs.Url(e.Uri,out target);};
                web.NavigationCompleted+=(s,e)=>{
                    capture.Enabled=e.IsSuccess;
                    if(e.IsSuccess) {loading.Visible=false;browser.Visible=true;}
                    else {browser.Visible=false;loading.Visible=true;loading.Text="Не удалось открыть страницу. Попробуйте «В браузере».";}
                };
                web.Navigate(address.AbsoluteUri);
            } catch(Exception) {
                if(!holder.IsDisposed)loading.Text="Встроенный просмотр недоступен. Проверьте установку Microsoft Edge WebView2 Runtime.";
            }
        }
    }
}



