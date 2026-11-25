using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

public class Floor : Panel {
    public event Action<int> Selected;
    public Func<int, Color> StatusColor;
    public int Current;
    readonly RectangleF[] hits = new RectangleF[6];
    readonly string[] labels = {"Офсет", "Цифровая печать", "Резка", "Фальцовка", "Ламинация", "Переплёт"};

    public Floor() {
        DoubleBuffered = true; ResizeRedraw=true;
        BackColor = Color.FromArgb(229,237,233);
        AccessibleName = "Изометрическая карта цеха. Оборудование также доступно в списке справа.";
        Cursor = Cursors.Hand;
    }

    PointF Project(float x, float y, float z) {
        float scale = Math.Min(Width / 600f, Height / 360f);
        return new PointF(Width / 2f + (x - y - 46) * scale, Height / 2f + (x + y - 270) * .47f * scale - z * scale + 18 * scale);
    }

    void Polygon(Graphics g, Color color, params PointF[] points) {
        using (var brush = new SolidBrush(color)) g.FillPolygon(brush, points);
    }

    void Box(Graphics g, float x, float y, float w, float d, float h, float z, Color top, Color left, Color right) {
        PointF a = Project(x,y,z+h), b = Project(x+w,y,z+h);
        PointF c = Project(x+w,y+d,z+h), e = Project(x,y+d,z+h);
        Polygon(g,left,e,c,Project(x+w,y+d,z),Project(x,y+d,z));
        Polygon(g,right,b,c,Project(x+w,y+d,z),Project(x+w,y,z));
        Polygon(g,top,a,b,c,e);
    }

    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Box(g, -12, -12, 333, 242, 10, -10, Color.FromArgb(170,188,182), Color.FromArgb(24,35,53), Color.FromArgb(20,31,47));
        using (var pen = new Pen(Color.FromArgb(184,199,193))) {
            for (int x=0;x<=300;x+=25) g.DrawLine(pen,Project(x,0,0),Project(x,220,0));
            for (int y=0;y<=220;y+=22) g.DrawLine(pen,Project(0,y,0),Project(300,y,0));
        }
        using (var font = new Font("Segoe UI", 9, FontStyle.Bold))
        using (var fmt = new StringFormat { Alignment = StringAlignment.Center })
        for (int row=0; row<2; row++) for (int col=0; col<3; col++) {
            int i = row*3+col;
            float x=col*104+9, y=row*112+9;
            Color status = StatusColor == null ? Color.MediumSeaGreen : StatusColor(i);
            Box(g,x,y,83,78,2,0,i==Current ? Color.FromArgb(147,205,184) : Color.FromArgb(207,219,214),Color.FromArgb(32,46,63),Color.FromArgb(32,46,63));
            Box(g,x+9,y+14,58,40,7,2,Color.FromArgb(55,71,91),Color.FromArgb(29,41,57),Color.FromArgb(37,49,65));
            int modules=i==0?4:i==1?2:i==5?3:1;
            for(int p=0;p<modules;p++){
                float w=52f/modules, xx=x+11+p*w;
                float h=i==2?9:i==3?13:i==4?11:20+(p%2)*3;
                Box(g,xx,y+17,w-1,34,h,9,Color.WhiteSmoke,Color.FromArgb(139,163,162),Color.FromArgb(85,111,111));
                if(i==0||i==1||i==5)Box(g,xx+2,y+21,w-5,20,2,9+h,Color.DarkSlateGray,Color.Teal,Color.DarkSlateGray);
            }
            if(i==2){
                Box(g,x+23,y+19,5,27,25,18,Color.WhiteSmoke,Color.SlateGray,Color.DarkSlateGray);
                Box(g,x+51,y+19,5,27,25,18,Color.WhiteSmoke,Color.SlateGray,Color.DarkSlateGray);
                Box(g,x+23,y+20,33,7,6,38,Color.Goldenrod,Color.DarkGoldenrod,Color.SlateGray);
            }
            if(i==3||i==4)for(int k=0;k<(i==3?4:2);k++)
                Box(g,x+19+k*(i==3?9:23),y+20,5,28,i==3?6+k*2:13,21,Color.Goldenrod,Color.SlateGray,Color.DarkSlateGray);
            Box(g,x+61,y+45,6,3,10,7,Color.DarkSlateGray,Color.SlateGray,Color.DarkSlateGray);
            Box(g,x+59,y+44,11,4,7,16,Color.DarkSlateGray,Color.LightSeaGreen,Color.DarkSlateGray);
            PointF pin=Project(x+35,y+29,48);
            using(var brush=new SolidBrush(status)){g.FillEllipse(brush,pin.X-7,pin.Y-7,14,14);g.DrawEllipse(Pens.DarkSlateGray,pin.X-7,pin.Y-7,14,14);}            Box(g,x+35,y+55,22,12,4,2,Color.FromArgb(245,230,192),Color.FromArgb(159,140,105),Color.FromArgb(184,164,120));
            PointF label = Project(x+41,y+68,1);
            g.DrawString(labels[i],font,Brushes.DarkSlateGray,label,fmt);
            PointF center=Project(x+41,y+36,17);
            float scale=Math.Min(Width/600f,Height/360f);
            hits[i]=new RectangleF(center.X-42*scale, center.Y-31*scale,84*scale,72*scale);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e) {
        base.OnMouseClick(e);
        for (int i=hits.Length-1;i>=0;i--)
            if (hits[i].Contains(e.Location)) {
                if (Selected != null) Selected(i);
                return;
            }
    }
}
