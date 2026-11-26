using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Polya {
    sealed partial class Workspace {
        internal const int PointerUpdate=0x245,PointerDown=0x246,PointerUp=0x247,PointerCaptureChanged=0x24c;
        internal const int Pen=3,Touch=2,PressureMask=1,Inverted=2,Eraser=4,Canceled=0x8000;
        uint penId;
        int penPointerTime;
        string beforePenEraser;
        readonly PenInfo[] penHistory=new PenInfo[64];

        [StructLayout(LayoutKind.Sequential)]
        internal struct PointerInfo {
            public int Type;
            public uint Id,Frame,Flags;
            public IntPtr Source,Target;
            public Point Pixel,Himetric,RawPixel,RawHimetric;
            public int Time;
            public uint Count;
            public int Input;
            public uint KeyStates;
            public ulong PerformanceCount;
            public int ButtonChange;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct PenInfo {
            public PointerInfo Pointer;
            public uint Flags,Mask,Pressure,Rotation;
            public int TiltX,TiltY;
        }
        [DllImport("user32.dll")]
        internal static extern bool GetPointerType(uint id,out int type);
        [DllImport("user32.dll")]
        internal static extern bool GetPointerPenInfo(uint id,out PenInfo info);
        [DllImport("user32.dll")]
        internal static extern bool GetPointerPenInfoHistory(uint id,ref uint entries,[Out] PenInfo[] data);

        protected override void WndProc(ref Message msg) {
            int type;
            uint id=(uint)(msg.WParam.ToInt64()&0xffff);
            if(msg.Msg==PointerCaptureChanged&&penId!=0&&id==penId) {EndPen();Invalidate();}
            if(msg.Msg==PointerDown||msg.Msg==PointerUpdate||msg.Msg==PointerUp) {
                if(GetPointerType(id,out type)&&type==Touch&&penId!=0)return;
                if(HandlePen(msg.Msg,id))return;
            }
            base.WndProc(ref msg);
        }
        bool HandlePen(int message,uint id) {
            if(page!=ScreenPage.Board||presenting)return false;
            PenInfo sample;
            if(message==PointerDown&&penId==0) {
                if(!GetPointerPenInfo(id,out sample))return false;
                var location=PointToClient(sample.Pointer.Pixel);
                if(At(Logical(location))!=null||popup!=null||surface.Dragging)return false;
                var tool=surface.Tool;
                bool ink=tool=="pen"||Surface.IsEraser(tool)||tablet.EraserEnd&&(sample.Flags&(Inverted|Eraser))!=0;
                if(!ink)return false;
                Focus();
                penId=id;penPointerTime=sample.Pointer.Time;
                surface.PenContact=true;
                if(tablet.EraserEnd&&(sample.Flags&(Inverted|Eraser))!=0) {beforePenEraser=tool;surface.Tool="eraser";}
                StopZoom();
                UpdatePenForce(sample);
                pointer=Logical(location);pointerSeen=true;
                surface.OnDown(pointer,false,false);UpdateCursor();
                if(surface.Inking)Dirty(R(pointer.X-16,pointer.Y-16,32,32));else Invalidate();
                return true;
            }
            if(penId==0||id!=penId)return false;
            if(!GetPointerPenInfo(id,out sample)) {if(message==PointerUp)EndPen();return true;}
            if((sample.Pointer.Flags&Canceled)!=0||!surface.Dragging) {
                EndPen();Invalidate();return true;
            }
            if(message==PointerUpdate) {
                var dirty=RectangleF.Empty;
                uint count=(uint)penHistory.Length;
                if(sample.Pointer.Count>1&&sample.Pointer.Count<=count&&GetPointerPenInfoHistory(id,ref count,penHistory)) {
                    for(int i=(int)count-1;i>=0;i--) {
                        if(penHistory[i].Pointer.Time<penPointerTime)continue;
                        var sampleDamage=TrackPen(penHistory[i]);
                        if(!sampleDamage.IsEmpty)dirty=dirty.IsEmpty?sampleDamage:RectangleF.Union(dirty,sampleDamage);
                    }
                }
                else dirty=TrackPen(sample);
                if(!dirty.IsEmpty)Dirty(dirty);
                penPointerTime=sample.Pointer.Time;
            } else if(message==PointerUp) {
                bool inking=surface.Inking;
                surface.OnUp(Logical(PointToClient(sample.Pointer.Pixel)),false);
                EndPen();if(!inking)ViewChanged();
            }
            return true;
        }
        RectangleF TrackPen(PenInfo sample) {
            UpdatePenForce(sample);
            pointer=Logical(PointToClient(sample.Pointer.Pixel));pointerSeen=true;
            return surface.Track(pointer,false);
        }
        void UpdatePenForce(PenInfo sample) {
            surface.PenPressure=!tablet.Pressure||(sample.Mask&PressureMask)==0?1:.25f+.75f*Math.Min(1024,sample.Pressure)/1024f;
        }
        void EndPen() {
            if(penId==0)return;
            penId=0;surface.PenContact=false;surface.PenPressure=1;
            if(surface.Dragging)surface.Cancel();
            if(beforePenEraser!=null){surface.Tool=beforePenEraser;beforePenEraser=null;}
            UpdateCursor();
        }
    }
}
