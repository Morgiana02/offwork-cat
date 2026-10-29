using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

public static class AutoStart {
    const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    const string ValueName="OffworkCat";
    public static string CommandFor(string path){return "\""+path+"\" --startup";}
    public static bool Enabled(){using(var key=Registry.CurrentUser.OpenSubKey(RunKey))return key!=null&&!String.IsNullOrWhiteSpace(key.GetValue(ValueName) as string);}
    public static void SetEnabled(bool enabled){
        using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){
            if(key==null)throw new IOException("无法保存开机自启动设置。");
            if(enabled)key.SetValue(ValueName,CommandFor(Application.ExecutablePath),RegistryValueKind.String);
            else key.DeleteValue(ValueName,false);
        }
    }
}
public class DayPlan {
    public int Day,Start=510,Lunch=675,End=990;
    public bool Working;
    public DayPlan Copy(){return new DayPlan{Day=Day,Start=Start,Lunch=Lunch,End=End,Working=Working};}
    public bool Valid(){return Day>=0&&Day<=6&&(!Working||(Start>=0&&End<1440&&Start<Lunch&&Lunch<End));}
    public decimal Hours {get{return Working?(End-Start)/60m:0;}}
}
public class CatSettings {
    public int Start = 510, Lunch = 675, End = 990;
    public string LastLunchCelebration="", LastEndCelebration="";
    public int ScheduleVersion;
    public int[] Days = new int[] {1,2,3,4,5};
    public int X = int.MinValue, Y = int.MinValue;
    public float Scale = 1;
    public bool Clock = false, Top = true;
    public string Cat="dance";
    public bool RandomCats;
    public decimal MonthlySalary;
    public bool SalarySet;
    public bool AdvancedSchedule;
    public bool WeekConfigured;
    public DayPlan[] Week;
    public DayPlan Plan(int day){if(AdvancedSchedule&&Week!=null){var p=Week.FirstOrDefault(x=>x!=null&&x.Day==day);if(p!=null)return p;}return new DayPlan{Day=day,Start=Start,Lunch=Lunch,End=End,Working=Days!=null&&Days.Contains(day)};}
    public DayPlan[] Plans(){return Enumerable.Range(0,7).Select(d=>Plan(d).Copy()).ToArray();}
    public bool Valid() { return Start>=0 && Start<1440 && End>=0 && End<1440 && Start<Lunch && Lunch<End && Days!=null && Days.Length>0 && Days.All(x=>x>=0 && x<=6) && Scale>=.65f && Scale<=1.5f && MonthlySalary>=0 && MonthlySalary<=100000000m && (!AdvancedSchedule||(Week!=null&&Week.Length==7&&Week.All(x=>x!=null&&x.Valid())&&Week.Select(x=>x.Day).Distinct().Count()==7&&Week.Any(x=>x.Working))); }
}
public static class SalaryMath {
    public static decimal WeeklyHours(CatSettings settings){return settings.Plans().Sum(p=>p.Hours);}
    public static decimal MonthlyHours(CatSettings settings){return WeeklyHours(settings)/7m*30m;}
    public static decimal Hourly(CatSettings settings){decimal weekly=WeeklyHours(settings);return weekly>0?settings.MonthlySalary*7m/(weekly*30m):0;}
    public static decimal Today(DateTime now,CatSettings settings){
        var plan=settings.Plan((int)now.DayOfWeek);
        if(!settings.SalarySet || !plan.Working)return 0;
        DateTime start=now.Date.AddMinutes(plan.Start),end=now.Date.AddMinutes(plan.End);
        DateTime stop=now<end?now:end;
        decimal hours=Math.Max(0,(decimal)(stop-start).Ticks/TimeSpan.TicksPerHour);
        return Hourly(settings)*hours;
    }
    public static string Money(decimal value){return value.ToString("0.00");}
}
public sealed class ClickThought {
    public DateTime Until=DateTime.MinValue;
    public int Page;
    public int Click(DateTime now){Page=now<Until?(Page+1)%2:0;Until=now.AddSeconds(3);return Page;}
}
public sealed class CatClip : IDisposable {
    public static readonly string[] Keys={"leisure","dance","thinking","fishing","happy","feet","reluctant","done","speed-off","angry"};
    public static readonly string[] Names={"休闲","跳舞","思考","摸鱼","开心","脚脚摸鱼","不想上班","不玩啦","光速下班","生气"};
    public readonly Bitmap[] Frames;
    public readonly int[] Durations;
    public readonly int TotalDuration;
    public CatClip(string key){
        using(var resource=typeof(CatClip).Assembly.GetManifestResourceStream("cat."+key)){
            if(resource==null)throw new Exception("Missing cat animation: "+key);
            using(var reader=new BinaryReader(resource)){
                int count=reader.ReadInt32();Frames=new Bitmap[count];Durations=new int[count];
                for(int i=0;i<count;i++){Durations[i]=reader.ReadInt32();int length=reader.ReadInt32();using(var bytes=new MemoryStream(reader.ReadBytes(length)))using(var image=Image.FromStream(bytes))Frames[i]=new Bitmap(image);TotalDuration+=Durations[i];}
            }
        }
    }
    public int IndexAt(long elapsed){long at=elapsed%TotalDuration;for(int i=0;i<Frames.Length;i++){if(at<Durations[i])return i;at-=Durations[i];}return 0;}
    public Image At(long elapsed){return Frames[IndexAt(elapsed)];}
    public void Dispose(){foreach(var frame in Frames)frame.Dispose();}
}
public class CelebrationTracker {
    DateTime previous;
    public CelebrationTracker(DateTime now){previous=now;}
    public void Reset(DateTime now){previous=now;}
    public string Poll(DateTime now,CatSettings s){
        DateTime since=previous;previous=now;
        var plan=s.Plan((int)now.DayOfWeek);
        if(now<since || !plan.Working)return null;
        string day=now.ToString("yyyy-MM-dd"),result=null;
        DateTime lunch=now.Date.AddMinutes(plan.Lunch),end=now.Date.AddMinutes(plan.End);
        if(since<lunch && now>=lunch && s.LastLunchCelebration!=day){s.LastLunchCelebration=day;result="lunch";}
        if(since<end && now>=end && s.LastEndCelebration!=day){s.LastEndCelebration=day;result="end";}
        return result;
    }
}
public class CreamMenu : ToolStripProfessionalRenderer {
    static readonly Color Paper=Color.FromArgb(255,250,239),Ink=Color.FromArgb(91,70,60),Line=Color.FromArgb(227,215,191),Hover=Color.FromArgb(240,229,204);
    readonly float scale;
    public CreamMenu(float dpi){scale=dpi;RoundedEdges=false;}
    public static void Apply(ToolStrip strip,float dpi){
        strip.Renderer=new CreamMenu(dpi);strip.BackColor=Paper;strip.ForeColor=Ink;strip.Font=new Font("Microsoft YaHei UI",12*dpi,FontStyle.Regular,GraphicsUnit.Pixel);
        strip.Padding=new Padding((int)(5*dpi));strip.ImageScalingSize=new Size((int)(16*dpi),(int)(16*dpi));
        foreach(ToolStripItem item in strip.Items){item.ForeColor=Ink;if(!(item is ToolStripSeparator))item.Padding=new Padding((int)(3*dpi),(int)(5*dpi),(int)(5*dpi),(int)(5*dpi));var drop=item as ToolStripDropDownItem;if(drop!=null&&drop.HasDropDownItems)Apply(drop.DropDown,dpi);}
    }
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(Paper);}
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){using(var b=new SolidBrush(Paper))e.Graphics.FillRectangle(b,e.AffectedBounds);}
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){using(var p=new Pen(Line))e.Graphics.DrawRectangle(p,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);}
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){
        var rect=new Rectangle(1,1,e.Item.Width-2,e.Item.Height-2);using(var b=new SolidBrush(e.Item.Selected?Hover:Paper))e.Graphics.FillRectangle(b,rect);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=e.Item.Enabled?Ink:Color.FromArgb(166,149,128);base.OnRenderItemText(e);}
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e){
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;Rectangle r=e.ImageRectangle;float side=12*scale,x=r.X+(r.Width-side)/2,y=r.Y+(r.Height-side)/2;
        using(var b=new SolidBrush(Color.FromArgb(226,232,208)))e.Graphics.FillEllipse(b,x-2*scale,y-2*scale,side+4*scale,side+4*scale);
        using(var p=new Pen(Color.FromArgb(111,132,85),1.7f*scale)){p.StartCap=p.EndCap=LineCap.Round;e.Graphics.DrawLines(p,new[]{new PointF(x+2*scale,y+6*scale),new PointF(x+5*scale,y+9*scale),new PointF(x+10*scale,y+3*scale)});}
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e){e.ArrowColor=Ink;base.OnRenderArrow(e);}
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using(var p=new Pen(Line))e.Graphics.DrawLine(p,8*scale,e.Item.Height/2,e.Item.Width-8*scale,e.Item.Height/2);}
}
public class WorkState {
    public string Kind;
    public TimeSpan Remaining;
    public double Progress;
    public static double ElapsedFraction(DateTime now,CatSettings settings=null) {
        var p=(settings??new CatSettings()).Plan((int)now.DayOfWeek);
        return p.Working?Math.Max(0,Math.Min(1,(now-now.Date.AddMinutes(p.Start)).TotalMinutes/(p.End-p.Start))):0;
    }
    public static string Countdown(DateTime now, CatSettings s, WorkState state) {
        if(state.Kind=="rest")return "今天休息，好好躺平～";
        if(state.Kind=="done")return "今天已下班啦！";
        var plan=s.Plan((int)now.DayOfWeek);
        bool lunch=now.TimeOfDay<TimeSpan.FromMinutes(plan.Lunch);
        TimeSpan left=lunch?now.Date.AddMinutes(plan.Lunch)-now:state.Remaining;
        int seconds=Math.Max(0,(int)Math.Ceiling(left.TotalSeconds));
        return String.Format("距离{0}还有{1}小时{2}分{3}秒",lunch?"午饭":"下班",seconds/3600,seconds/60%60,seconds%60);
    }
    public static string Percentage(DateTime now) {
        double p=ElapsedFraction(now)*100;
        return "已上班 "+p.ToString("0.0")+"%";
    }
    public static WorkState Get(DateTime now, CatSettings s) {
        var plan=s.Plan((int)now.DayOfWeek);
        if(plan.Working) {
            DateTime start=now.Date.AddMinutes(plan.Start),end=now.Date.AddMinutes(plan.End);

            if(now>=start && now<end)return new WorkState{Kind="work",Remaining=end-now,Progress=(now-start).TotalSeconds/(end-start).TotalSeconds};
            if(now<start)return new WorkState{Kind="before",Remaining=end-now};
            if(now>=end)return new WorkState{Kind="done",Progress=1};
        }
        return new WorkState{Kind="rest"};
    }
}
static class Program {
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread] static int Main(string[] args) {
        if(args.Contains("--self-test"))return Tests();
        if(args.Length==2 && args[0]=="--render-check")return RenderCheck(args[1]);
        if(args.Length==2 && args[0]=="--layout-check"){try{return LayoutCheck(args[1]);}catch(Exception error){File.WriteAllText(Path.Combine(args[1],"layout-error.txt"),error.Message+"\n"+error.StackTrace);return 1;}}
        if(args.Length==2 && args[0]=="--menu-check")return MenuCheck(args[1]);
        bool first;
        using(var mutex=new Mutex(true,"Local\\OffworkCatNativeV1",out first)) {
            if(!first){if(!args.Contains("--startup"))MessageBox.Show("猫猫已经在运行啦。\n可在任务栏右下角托盘中找到它。","猫猫等下班");return 0;}
            SetProcessDPIAware(); Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CatForm());
        }
        return 0;
    }
    static int Tests() {
        var s=new CatSettings();int count=0;
        if(AutoStart.CommandFor("C:\\My Pets\\猫猫.exe")!="\"C:\\My Pets\\猫猫.exe\" --startup")throw new Exception("Quoted startup command");count++;
        Action<string,string,int> check=(time,kind,seconds)=>{var w=WorkState.Get(DateTime.Parse(time),s);if(w.Kind!=kind || (int)w.Remaining.TotalSeconds!=seconds)throw new Exception(time+": "+w.Kind);count++;};
        check("2026-10-22 08:00:00","before",30600);check("2026-10-22 08:30:00","work",28800);check("2026-10-22 16:29:59","work",1);check("2026-10-22 16:30:00","done",0);check("2026-10-24 12:00:00","rest",0);
        foreach(var sample in new[]{new[]{"2026-10-23 09:00:00","距离午饭还有2小时15分0秒"},new[]{"2026-10-23 11:14:59","距离午饭还有0小时0分1秒"},new[]{"2026-10-23 11:15:00","距离下班还有5小时15分0秒"},new[]{"2026-10-23 12:30:00","距离下班还有4小时0分0秒"},new[]{"2026-10-23 16:29:59","距离下班还有0小时0分1秒"},new[]{"2026-10-23 16:30:00","今天已下班啦！"}}){DateTime t=DateTime.Parse(sample[0]);if(WorkState.Countdown(t,s,WorkState.Get(t,s))!=sample[1])throw new Exception(sample[0]);count++;}
        foreach(var sample in new[]{new[]{"08:00","已上班 0.0%"},new[]{"08:30","已上班 0.0%"},new[]{"12:30","已上班 50.0%"},new[]{"16:30","已上班 100.0%"},new[]{"18:00","已上班 100.0%"}}){if(WorkState.Percentage(DateTime.Parse("2026-10-23 "+sample[0]))!=sample[1])throw new Exception("percentage");count++;}
        s.Lunch=720;DateTime changed=DateTime.Parse("2026-10-23 11:30:00");
        if(WorkState.Countdown(changed,s,WorkState.Get(changed,s))!="距离午饭还有0小时30分0秒")throw new Exception("custom lunch");count++;
        var tracker=new CelebrationTracker(DateTime.Parse("2026-10-23 11:59:59"));
        if(tracker.Poll(DateTime.Parse("2026-10-23 12:00:00"),s)!="lunch")throw new Exception("lunch boundary");count++;
        if(tracker.Poll(DateTime.Parse("2026-10-23 12:00:01"),s)!=null)throw new Exception("duplicate lunch");count++;
        if(tracker.Poll(DateTime.Parse("2026-10-23 16:30:00"),s)!="end")throw new Exception("end boundary");count++;
        if(tracker.Poll(DateTime.Parse("2026-10-23 16:30:01"),s)!=null)throw new Exception("duplicate end");count++;
        s.Cat="feet";s.RandomCats=true;s.MonthlySalary=8700m;s.SalarySet=true;
        using(var stream=new MemoryStream()){var serializer=new XmlSerializer(typeof(CatSettings));serializer.Serialize(stream,s);stream.Position=0;var restored=(CatSettings)serializer.Deserialize(stream);if(restored.Lunch!=720||restored.LastLunchCelebration!="2026-10-23"||restored.Cat!="feet"||!restored.RandomCats||restored.MonthlySalary!=8700m||!restored.SalarySet)throw new Exception("persist settings");tracker=new CelebrationTracker(DateTime.Parse("2026-10-23 11:59:59"));if(tracker.Poll(DateTime.Parse("2026-10-23 12:00:00"),restored)!=null)throw new Exception("repeat after restart");count++;}
        tracker=new CelebrationTracker(DateTime.Parse("2026-10-24 11:00:00"));if(tracker.Poll(DateTime.Parse("2026-10-24 17:00:00"),s)!=null)throw new Exception("rest celebration");count++;
        s.Start=1320;s.End=360;if(s.Valid())throw new Exception("overnight accepted");count++;
        s=new CatSettings();s.Lunch=s.End;if(s.Valid())throw new Exception("invalid lunch");count++;
        s=new CatSettings();s.Days=new int[0];if(s.Valid())throw new Exception("empty days");count++;
        var pay=new CatSettings{MonthlySalary=12000m,SalarySet=true};
        if(SalaryMath.WeeklyHours(pay)!=40m||SalaryMath.Hourly(pay)!=70m)throw new Exception("salary rates");count++;
        foreach(var sample in new[]{new[]{"2026-10-23 08:00:00","0.00"},new[]{"2026-10-23 08:30:00","0.00"},new[]{"2026-10-23 09:30:00","70.00"},new[]{"2026-10-23 11:15:00","192.50"},new[]{"2026-10-23 12:30:00","280.00"},new[]{"2026-10-23 16:30:00","560.00"},new[]{"2026-10-23 18:30:00","560.00"},new[]{"2026-10-24 12:30:00","0.00"}}){if(SalaryMath.Money(SalaryMath.Today(DateTime.Parse(sample[0]),pay))!=sample[1])throw new Exception("earned: "+sample[0]);count++;}
        pay.End=1110;if(SalaryMath.Today(DateTime.Parse("2026-10-23 18:30:00"),pay)!=560m)throw new Exception("ten-hour schedule earnings");count++;
        pay.End=930;if(SalaryMath.Today(DateTime.Parse("2026-10-23 16:30:00"),pay)!=560m)throw new Exception("shorter daily schedule");count++;
        pay.SalarySet=false;if(SalaryMath.Today(DateTime.Parse("2026-10-23 12:30:00"),pay)!=0m)throw new Exception("unset salary");count++;
        var advancedPay=new CatSettings{MonthlySalary=11400m,SalarySet=true};advancedPay.Week=advancedPay.Plans();advancedPay.AdvancedSchedule=true;
        var friday=advancedPay.Week.First(p=>p.Day==5);friday.End=870;
        var tuesday=advancedPay.Week.First(p=>p.Day==2);tuesday.Start=540;tuesday.Lunch=720;tuesday.End=1020;
        if(!advancedPay.Valid()||SalaryMath.WeeklyHours(advancedPay)!=38m||SalaryMath.Hourly(advancedPay)!=70m)throw new Exception("weekly salary basis");count++;
        foreach(var sample in new[]{new[]{"2026-10-19 16:30:00","560.00"},new[]{"2026-10-20 08:45:00","0.00"},new[]{"2026-10-20 17:00:00","560.00"},new[]{"2026-10-23 11:30:00","210.00"},new[]{"2026-10-23 14:30:00","420.00"},new[]{"2026-10-24 12:00:00","0.00"}}){if(SalaryMath.Money(SalaryMath.Today(DateTime.Parse(sample[0]),advancedPay))!=sample[1])throw new Exception("weekly earnings "+sample[0]);count++;}
        var tue=DateTime.Parse("2026-10-20 11:30:00");if(WorkState.Countdown(tue,advancedPay,WorkState.Get(tue,advancedPay))!="距离午饭还有0小时30分0秒")throw new Exception("weekday lunch");count++;
        if(WorkState.Get(DateTime.Parse("2026-10-23 14:30:00"),advancedPay).Kind!="done"||WorkState.ElapsedFraction(DateTime.Parse("2026-10-23 11:30:00"),advancedPay)!=.5)throw new Exception("weekday progress");count++;
        var weeklyCelebration=new CelebrationTracker(DateTime.Parse("2026-10-23 14:29:59"));if(weeklyCelebration.Poll(DateTime.Parse("2026-10-23 14:30:00"),advancedPay)!="end")throw new Exception("weekday celebration");count++;
        using(var bytes=new MemoryStream()){var serializer=new XmlSerializer(typeof(CatSettings));serializer.Serialize(bytes,advancedPay);bytes.Position=0;var restored=(CatSettings)serializer.Deserialize(bytes);if(!restored.AdvancedSchedule||!restored.Valid()||restored.Plan(5).End!=870||SalaryMath.Hourly(restored)!=70m)throw new Exception("persist weekly schedule");count++;}
        var invalidWeek=advancedPay.Week.Select(p=>p.Copy()).ToArray();invalidWeek[0].Day=invalidWeek[1].Day;advancedPay.Week=invalidWeek;if(advancedPay.Valid())throw new Exception("duplicate weekday accepted");count++;
        var sevenDays=new CatSettings{Days=Enumerable.Range(0,7).ToArray(),MonthlySalary=12000m,SalarySet=true};
        if(SalaryMath.WeeklyHours(sevenDays)!=56m||SalaryMath.MonthlyHours(sevenDays)!=240m||SalaryMath.Hourly(sevenDays)!=50m)throw new Exception("7 calendar days and 30 days basis");count++;
        if(SalaryMath.Hourly(sevenDays)*SalaryMath.WeeklyHours(sevenDays)*30m/7m!=sevenDays.MonthlySalary)throw new Exception("monthly salary reconstruction");count++;
        var clicks=new ClickThought();DateTime clicked=DateTime.Parse("2026-10-23 09:00:00");
        if(clicks.Click(clicked)!=0||(clicks.Until-clicked).TotalSeconds!=3||clicks.Click(clicked.AddMilliseconds(500))!=1||clicks.Click(clicked.AddSeconds(1))!=0||clicks.Click(clicked.AddSeconds(1.5))!=1||clicks.Click(clicked.AddSeconds(4.5))!=0)throw new Exception("click cycle/expiry");count++;
        foreach(string key in CatClip.Keys)using(var clip=new CatClip(key)){if(clip.Frames.Length<2||clip.IndexAt(0)!=0||clip.IndexAt(clip.TotalDuration)!=0||clip.IndexAt(clip.Durations[0])!=1||clip.Frames.Any(f=>f.Width!=120||f.Height!=120))throw new Exception("animation validation: "+key);count++;}
        Console.WriteLine(count+" time/settings/celebration/animation checks passed.");return 0;
    }
    static int RenderCheck(string path) {
        using(var bitmap=new Bitmap(420,220,PixelFormat.Format32bppPArgb)) {
            bitmap.SetResolution(96,96);
            using(var g=Graphics.FromImage(bitmap))using(var font=new Font("Microsoft YaHei UI",11f,FontStyle.Regular,GraphicsUnit.Pixel))using(var brush=new SolidBrush(Color.FromArgb(91,70,60))) {
                g.Clear(Color.FromArgb(255,249,236));g.ScaleTransform(2,2);g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
                string[] lines={"距离午饭还有2小时15分0秒","距离下班还有4小时0分0秒","已上班 50.0%"};
                for(int i=0;i<lines.Length;i++) {
                    if(g.MeasureString(lines[i],font).Width>176)throw new Exception("Text does not fit");
                    CatForm.DrawLabel(g,lines[i],font,brush,new RectangleF(15,i*35,180,30));
                }
            }
            bitmap.Save(path,ImageFormat.Png);
        }
        Console.WriteLine("Pixel-font rendering and width checks passed at 200% scale.");return 0;
    }
    static int MenuCheck(string path){
        SetProcessDPIAware();Application.EnableVisualStyles();
        using(var menu=new ContextMenuStrip()){
            menu.Items.Add("猫猫等下班  ฅ");menu.Items.Add(new ToolStripSeparator());menu.Items.Add("设置作息时间…");menu.Items.Add("显示当前时钟");menu.Items.Add(new ToolStripMenuItem("始终置顶"){Checked=true});
            var sizes=new ToolStripMenuItem("大小");sizes.DropDownItems.Add("标准 · 210 × 160");menu.Items.Add(sizes);menu.Items.Add("暂停猫猫动画");menu.Items.Add("预览午饭庆祝");menu.Items.Add("预览下班庆祝");menu.Items.Add("回到右下角");menu.Items.Add(new ToolStripSeparator());menu.Items.Add("暂时藏起来");menu.Items.Add("退出猫猫");CreamMenu.Apply(menu,1.5f);
            menu.Show(new Point(-20000,-20000));Application.DoEvents();using(var bmp=new Bitmap(menu.Width,menu.Height)){menu.DrawToBitmap(bmp,new Rectangle(Point.Empty,menu.Size));bmp.Save(path);}menu.Close();
        }
        Console.WriteLine("Cream menu rendered at 150% scale.");return 0;
    }
    static int LayoutCheck(string directory) {
        SetProcessDPIAware();Application.EnableVisualStyles();Directory.CreateDirectory(directory);
        foreach(float scale in new float[]{1f,1.5f,2f})using(var form=new SettingsForm(new CatSettings(),scale)){
            form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();form.PerformLayout();Application.DoEvents();
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(directory,"settings-"+(int)(scale*100)+".png"));}
            CheckLayout(form);Console.WriteLine("Settings layout checked at "+(int)(scale*100)+"%: "+form.ClientSize);
        }
        foreach(float scale in new float[]{1f,1.5f,2f})using(var form=new SalaryForm(new CatSettings{MonthlySalary=8700m,SalarySet=true},scale)){
            form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();form.PerformLayout();Application.DoEvents();
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(directory,"salary-"+(int)(scale*100)+".png"));}
            CheckLayout(form);Console.WriteLine("Salary layout checked at "+(int)(scale*100)+"%: "+form.ClientSize);
        }
        foreach(float scale in new float[]{1f,1.5f,2f})using(var form=new AdvancedScheduleForm(new CatSettings(),scale)){
            form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();form.PerformLayout();Application.DoEvents();
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(directory,"advanced-"+(int)(scale*100)+".png"));}
            CheckLayout(form);Console.WriteLine("Advanced schedule layout checked at "+(int)(scale*100)+"%: "+form.ClientSize);
        }
        string[][] moneyThoughts={new[]{"earnings-thought.png","今天已赚","200.00 元"},new[]{"end-earnings.png","下班啦！辛苦了～","今天赚了 400.00 元"}};
        foreach(var example in moneyThoughts)using(var bitmap=new Bitmap(420,152,PixelFormat.Format32bppPArgb)){
            bitmap.SetResolution(96,96);using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);g.ScaleTransform(2,2);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;CatForm.DrawThought(g,DateTime.Now,example[1],example[2]);}bitmap.Save(Path.Combine(directory,example[0]));
        }
        using(var bitmap=new Bitmap(420,152,PixelFormat.Format32bppPArgb)){
            bitmap.SetResolution(96,96);using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);g.ScaleTransform(2,2);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;CatForm.DrawThought(g,DateTime.Parse("2026-10-23 09:20:24"),null);}
            bitmap.Save(Path.Combine(directory,"thought.png"));
        }
        return 0;
    }
    static void CheckLayout(Control parent){
        foreach(Control c in parent.Controls){if(c.Left<0||c.Top<0||c.Right>parent.ClientSize.Width+1||c.Bottom>parent.ClientSize.Height+1)throw new Exception("Control clipped: "+c.Text+" bounds "+c.Bounds+" parent "+parent.ClientSize);CheckLayout(c);}
    }
}
public class CatForm : Form {
    CatSettings cfg;
    readonly string settingsPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.xml");
    readonly Dictionary<string,CatClip> clips=new Dictionary<string,CatClip>();
    string activeCat,celebrationCat="dance";
    Image cat {get{return clips[activeCat].At(animationMillis);}}
    long animationMillis,lastAnimationTick;
    readonly Stopwatch animationClock=Stopwatch.StartNew();
    readonly Random random=new Random();
    DateTime nextRandomCat=DateTime.Now.AddMinutes(1);
    readonly Dictionary<string,ToolStripMenuItem> catItems=new Dictionary<string,ToolStripMenuItem>();
    ToolStripMenuItem randomCatItem;
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly NotifyIcon tray=new NotifyIcon();
    readonly Font small=new Font("Microsoft YaHei UI",11f,FontStyle.Regular,GraphicsUnit.Pixel),clockFont=new Font("Consolas",13f,FontStyle.Regular,GraphicsUnit.Pixel);
    readonly Color ink=Color.FromArgb(91,70,60), green=Color.FromArgb(123,145,99);
    readonly ContextMenuStrip menu=new ContextMenuStrip();
    ToolStripMenuItem clockItem,topItem,pauseItem;
    int ticks;bool dragging,moved,animate=true;Point dragStart,origin;DateTime messageUntil,previewUntil;
    string message="";bool showProgressThought,showEarningsCelebration;
    decimal celebrationEarnings;
    readonly ClickThought clickThought=new ClickThought();
    readonly CelebrationTracker celebrations=new CelebrationTracker(DateTime.Now);
    float dpi=1;
    public CatForm() {
        cfg=LoadSettings();Text="猫猫等下班 · 桌面宠物";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None;TopMost=cfg.Top;DoubleBuffered=true;
        using(Graphics g=Graphics.FromHwnd(IntPtr.Zero))dpi=g.DpiX/96f;
        foreach(string key in CatClip.Keys)clips.Add(key,new CatClip(key));if(!clips.ContainsKey(cfg.Cat))cfg.Cat="dance";activeCat=cfg.Cat;
        using(Bitmap iconImage=new Bitmap(32,32)){using(Graphics g=Graphics.FromImage(iconImage))g.DrawImage(cat,0,0,32,32);IntPtr h=iconImage.GetHicon();try{Icon=(Icon)System.Drawing.Icon.FromHandle(h).Clone();}finally{Native.DestroyIcon(h);}}
        menu.Items.Add("猫猫等下班  ฅ",null,(o,e)=>Say("再坚持一下喵。"));menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("设置作息时间…",null,(o,e)=>Settings());
        menu.Items.Add("设置工资…",null,(o,e)=>SalarySettings());
        var catsMenu=new ToolStripMenuItem("换一只猫猫");
        for(int i=0;i<CatClip.Keys.Length;i++){string key=CatClip.Keys[i];var item=new ToolStripMenuItem(CatClip.Names[i]);item.Click+=(o,e)=>ChooseCat(key);catItems.Add(key,item);catsMenu.DropDownItems.Add(item);}
        catsMenu.DropDownItems.Add(new ToolStripSeparator());randomCatItem=new ToolStripMenuItem("随机换猫猫（每分钟）");randomCatItem.Click+=(o,e)=>{cfg.RandomCats=!cfg.RandomCats;nextRandomCat=DateTime.Now.AddMinutes(1);if(cfg.RandomCats)RandomCat();RefreshCatChecks();Save();};catsMenu.DropDownItems.Add(randomCatItem);menu.Items.Add(catsMenu);RefreshCatChecks();
        clockItem=new ToolStripMenuItem("显示当前时钟");clockItem.Checked=cfg.Clock;clockItem.Click+=(o,e)=>{cfg.Clock=!cfg.Clock;clockItem.Checked=cfg.Clock;ResizePet();Save();};menu.Items.Add(clockItem);
        topItem=new ToolStripMenuItem("始终置顶");topItem.Checked=cfg.Top;topItem.Click+=(o,e)=>{cfg.Top=!cfg.Top;TopMost=cfg.Top;topItem.Checked=cfg.Top;Save();};menu.Items.Add(topItem);
        var startupItem=new ToolStripMenuItem("开机自启动");
        Action refreshStartup=()=>{try{startupItem.Checked=AutoStart.Enabled();startupItem.Enabled=true;}catch{startupItem.Checked=false;startupItem.Enabled=false;}};
        startupItem.Click+=(o,e)=>{try{AutoStart.SetEnabled(!AutoStart.Enabled());refreshStartup();}catch(Exception error){MessageBox.Show("自启动设置未能保存："+error.Message,"猫猫等下班",MessageBoxButtons.OK,MessageBoxIcon.Information);refreshStartup();}};
        menu.Items.Add(startupItem);menu.Opening+=(o,e)=>refreshStartup();refreshStartup();
        var sizes=new ToolStripMenuItem("大小");foreach(float scale in new float[]{.75f,1f,1.25f}){float selected=scale;var item=new ToolStripMenuItem(scale==1?"标准 · 210 × 160":""+(int)(scale*100)+"%");item.Click+=(o,e)=>{cfg.Scale=selected;ResizePet();Save();};sizes.DropDownItems.Add(item);}menu.Items.Add(sizes);
        pauseItem=new ToolStripMenuItem("暂停猫猫动画");pauseItem.Click+=(o,e)=>{animate=!animate;pauseItem.Checked=!animate;};menu.Items.Add(pauseItem);
        menu.Items.Add("预览午饭庆祝",null,(o,e)=>Celebrate("lunch"));
        menu.Items.Add("预览下班庆祝",null,(o,e)=>Celebrate("end"));
        menu.Items.Add("回到右下角",null,(o,e)=>{PlaceDefault();Save();});
        menu.Items.Add(new ToolStripSeparator());menu.Items.Add("暂时藏起来",null,(o,e)=>Hide());menu.Items.Add("退出猫猫",null,(o,e)=>Close());
        CreamMenu.Apply(menu,dpi);
        ContextMenuStrip=menu;tray.Icon=Icon;tray.Text="猫猫等下班 · 右键设置，双击找回";tray.ContextMenuStrip=menu;tray.Visible=true;tray.DoubleClick+=(o,e)=>{Show();TopMost=cfg.Top;};
        ResizePet();if(cfg.X==int.MinValue)PlaceDefault();else{Location=new Point(cfg.X,cfg.Y);ClampPosition();}
        timer.Interval=40;timer.Tick+=(o,e)=>{ticks++;DateTime now=DateTime.Now;var milestone=celebrations.Poll(now,cfg);if(milestone!=null){Celebrate(milestone);Save();}if(cfg.RandomCats&&now>=nextRandomCat){RandomCat();nextRandomCat=now.AddMinutes(1);Save();}long tick=animationClock.ElapsedMilliseconds;if(animate||now<previewUntil)animationMillis+=Math.Min(250,tick-lastAnimationTick);lastAnimationTick=tick;Render();};
        Shown+=(o,e)=>{lastAnimationTick=animationClock.ElapsedMilliseconds;Render();timer.Start();};
        MouseDown+=(o,e)=>{if(e.Button==MouseButtons.Left){dragging=true;moved=false;dragStart=Cursor.Position;origin=Location;Capture=true;}};
        MouseMove+=(o,e)=>{if(dragging){Point p=Cursor.Position;int dx=p.X-dragStart.X,dy=p.Y-dragStart.Y;if(Math.Abs(dx)+Math.Abs(dy)>4)moved=true;if(moved)Location=new Point(origin.X+dx,origin.Y+dy);}};
        MouseUp+=(o,e)=>{if(e.Button!=MouseButtons.Left)return;dragging=false;Capture=false;if(moved){ClampPosition();Save();}else Think();};
        FormClosing+=(o,e)=>{Save();timer.Stop();tray.Visible=false;tray.Dispose();foreach(var clip in clips.Values)clip.Dispose();small.Dispose();clockFont.Dispose();menu.Dispose();timer.Dispose();};
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x80000|0x80;return p;}}
    CatSettings LoadSettings(){try{using(var f=File.OpenRead(settingsPath)){var s=(CatSettings)new XmlSerializer(typeof(CatSettings)).Deserialize(f);if(s.ScheduleVersion<2){s.Start=510;s.End=990;s.ScheduleVersion=2;}if(s.Valid())return s;}}catch{}return new CatSettings{ScheduleVersion=2};}
    void Save(){cfg.X=Left;cfg.Y=Top;try{string temp=settingsPath+".tmp";using(var f=File.Create(temp))new XmlSerializer(typeof(CatSettings)).Serialize(f,cfg);if(File.Exists(settingsPath))File.Replace(temp,settingsPath,null);else File.Move(temp,settingsPath);}catch{tray.Text="猫猫等下班 · 设置暂未保存";}}
    int ThoughtOffset {get{return DateTime.Now<messageUntil?62:0;}}
    void ResizePet(){int oldHeight=Height;Size=new Size((int)(210*cfg.Scale*dpi),(int)(((cfg.Clock?184:160)+ThoughtOffset)*cfg.Scale*dpi));if(oldHeight>0 && oldHeight!=Height)Top+=oldHeight-Height;ClampPosition();if(IsHandleCreated)Render();}
    void PlaceDefault(){Rectangle r=Screen.PrimaryScreen.WorkingArea;Location=new Point(r.Right-Width-28,r.Bottom-Height-18);}
    void ClampPosition(){Rectangle current=new Rectangle(Location,Size);if(!Screen.AllScreens.Any(s=>Rectangle.Intersect(s.WorkingArea,current).Width>=Math.Min(Width,50)&&Rectangle.Intersect(s.WorkingArea,current).Height>=Math.Min(Height,50)))PlaceDefault();}
    void Say(string text){message=text;showProgressThought=false;messageUntil=DateTime.Now.AddSeconds(4);ResizePet();}
    void Think(){DateTime now=DateTime.Now;clickThought.Click(now);showProgressThought=true;showEarningsCelebration=false;previewUntil=DateTime.MinValue;messageUntil=clickThought.Until;ResizePet();}
    void ChooseCat(string key){cfg.Cat=key;cfg.RandomCats=false;previewUntil=DateTime.MinValue;messageUntil=DateTime.MinValue;RefreshCatChecks();Save();ResizePet();}
    void RandomCat(){string[] choices=CatClip.Keys.Where(k=>k!=cfg.Cat).ToArray();cfg.Cat=choices[random.Next(choices.Length)];RefreshCatChecks();}
    void RefreshCatChecks(){foreach(var pair in catItems)pair.Value.Checked=pair.Key==cfg.Cat;randomCatItem.Checked=cfg.RandomCats;}
    static GraphicsPath Round(RectangleF r,float radius){GraphicsPath p=new GraphicsPath();float d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    void Label(Graphics g,string text,Font font,Brush brush,RectangleF r){DrawLabel(g,text,font,brush,r);}
    public static void DrawLabel(Graphics g,string text,Font font,Brush brush,RectangleF r){
        using(var sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap}){
            float size=font.Size;
            while(size>9 && g.MeasureString(text,font,int.MaxValue,sf).Width>r.Width-4){size-=.5f;using(var fit=new Font(font.FontFamily,size,font.Style,GraphicsUnit.Pixel)){if(g.MeasureString(text,fit,int.MaxValue,sf).Width<=r.Width-4){g.DrawString(text,fit,brush,r,sf);return;}}}
            if(size<font.Size){using(var fit=new Font(font.FontFamily,size,font.Style,GraphicsUnit.Pixel))g.DrawString(text,fit,brush,r,sf);}else g.DrawString(text,font,brush,r,sf);
        }
    }
    public static void DrawThought(Graphics g,DateTime now,string text,string detail=null,CatSettings settings=null){
        Color ink=Color.FromArgb(91,70,60),paper=Color.FromArgb(255,250,235),outline=Color.FromArgb(222,209,184);
        using(var path=new GraphicsPath(FillMode.Winding)){
            path.AddEllipse(8,12,31,30);path.AddEllipse(22,3,39,34);path.AddEllipse(47,0,43,35);path.AddEllipse(78,0,43,34);path.AddEllipse(107,2,43,34);path.AddEllipse(137,8,38,33);path.AddEllipse(158,19,29,27);path.AddEllipse(23,29,40,25);path.AddEllipse(53,29,46,25);path.AddEllipse(88,29,45,25);path.AddEllipse(120,28,50,25);path.AddRectangle(new RectangleF(25,20,145,22));
            using(var b=new SolidBrush(paper))g.FillPath(b,path);
        }
        using(var b=new SolidBrush(paper)){g.FillEllipse(b,118,56,12,9);g.FillEllipse(b,111,68,6,5);}
        using(var font=new Font("Microsoft YaHei UI",11f,FontStyle.Regular,GraphicsUnit.Pixel))using(var brush=new SolidBrush(ink)){
            if(text!=null){
                if(detail==null)DrawLabel(g,text,font,brush,new RectangleF(24,12,146,30));
                else{DrawLabel(g,text,font,brush,new RectangleF(24,7,146,20));DrawLabel(g,detail,font,brush,new RectangleF(24,27,146,22));}
                return;
            }
            var pie=new RectangleF(30,13,29,29);using(var track=new SolidBrush(Color.FromArgb(232,225,207)))g.FillEllipse(track,pie);
            float sweep=(float)(360*WorkState.ElapsedFraction(now,settings));using(var fill=new SolidBrush(Color.FromArgb(151,170,118))){if(sweep>=360)g.FillEllipse(fill,pie);else if(sweep>0)g.FillPie(fill,30,13,29,29,-90,sweep);}
            DrawLabel(g,"已上班",font,brush,new RectangleF(66,8,94,18));
            using(var valueFont=new Font("Consolas",15f,FontStyle.Bold,GraphicsUnit.Pixel))DrawLabel(g,(WorkState.ElapsedFraction(now,settings)*100).ToString("0.0")+"%",valueFont,brush,new RectangleF(66,26,94,22));
        }
    }
    void Render(){
        if(!IsHandleCreated||IsDisposed||Width<1||Height<1)return;
        DateTime now=DateTime.Now;WorkState w=WorkState.Get(now,cfg);bool celebrating=now<previewUntil;
        string desired=celebrating?celebrationCat:now<messageUntil&&showProgressThought?"thinking":cfg.Cat;if(desired!=activeCat){activeCat=desired;animationMillis=0;}
        int offset=ThoughtOffset;int expectedHeight=(int)(((cfg.Clock?184:160)+offset)*cfg.Scale*dpi);
        if(Height!=expectedHeight){ResizePet();return;}
        using(Bitmap bmp=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb)){
            bmp.SetResolution(96,96);
            using(Graphics g=Graphics.FromImage(bmp)){
                g.Clear(Color.Transparent);g.ScaleTransform(cfg.Scale*dpi,cfg.Scale*dpi);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
                if(offset>0){
                    if(showProgressThought && clickThought.Page>0){
                        if(!cfg.SalarySet)DrawThought(g,now,"还没告诉猫猫月薪哦","右键 → 设置工资");
                        else DrawThought(g,now,"今天已赚",SalaryMath.Money(SalaryMath.Today(now,cfg))+" 元");
                    }else if(celebrating && showEarningsCelebration)DrawThought(g,now,"下班啦！辛苦了～","今天赚了 "+SalaryMath.Money(celebrationEarnings)+" 元");
                    else DrawThought(g,now,showProgressThought?null:message,null,cfg);
                }
                g.TranslateTransform(0,offset);
                float bounce=celebrating?(float)Math.Sin(ticks*.5)*3:0;
                g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;g.DrawImage(cat,new RectangleF(53,5+bounce,104,104));g.PixelOffsetMode=PixelOffsetMode.Default;
                using(var path=Round(new RectangleF(7,116,196,41),14)){
                    using(var b=new SolidBrush(Color.FromArgb(255,255,249,236)))g.FillPath(b,path);
                    using(var pen=new Pen(Color.FromArgb(227,214,190),1))g.DrawPath(pen,path);
                }
                string time=WorkState.Countdown(now,cfg,w);
                using(var b=new SolidBrush(ink))Label(g,time,small,b,new RectangleF(15,120,180,30));
                if(cfg.Clock){using(var path=Round(new RectangleF(46,161,118,21),9)){using(var b=new SolidBrush(Color.FromArgb(255,250,245,231)))g.FillPath(b,path);}using(var b=new SolidBrush(ink))Label(g,now.ToString("HH:mm:ss"),clockFont,b,new RectangleF(46,161,118,21));}
                if(celebrating){for(int i=0;i<6;i++){float x=27+i*31,y=31+(ticks*2+i*19)%76;using(var b=new SolidBrush(i%2==0?Color.FromArgb(189,202,158):Color.FromArgb(231,187,159)))g.FillEllipse(b,x,y,4,4);}}
            }
            Native.Apply(Handle,bmp,Location);
        }
        string caption="猫猫等下班 · "+(w.Kind=="done"?"下班快乐":w.Kind=="rest"?"今天休息":String.Format("还剩 {0:00}:{1:00}:{2:00}",(int)w.Remaining.TotalHours,w.Remaining.Minutes,w.Remaining.Seconds));
        if(Text!=caption){Text=caption;tray.Text=caption.Length>63?caption.Substring(0,63):caption;}

    }
    void Celebrate(string kind){celebrationCat=kind=="lunch"?"happy":"speed-off";previewUntil=DateTime.Now.AddSeconds(8);message=kind=="lunch"?"开饭啦！猫猫先冲！":"下班啦！自由时间到！";showProgressThought=false;showEarningsCelebration=kind=="end"&&cfg.SalarySet;celebrationEarnings=SalaryMath.Today(DateTime.Today.AddMinutes(cfg.Plan((int)DateTime.Today.DayOfWeek).End),cfg);messageUntil=previewUntil;ResizePet();}
    void Settings(){using(var d=new SettingsForm(cfg)){d.StartPosition=FormStartPosition.CenterScreen;d.TopMost=true;if(d.ShowDialog()==DialogResult.OK){cfg.Start=d.Start;cfg.Lunch=d.Lunch;cfg.End=d.End;cfg.Days=d.Days;cfg.AdvancedSchedule=d.AdvancedSchedule;cfg.Week=d.Week;cfg.WeekConfigured=d.WeekConfigured;celebrations.Reset(DateTime.Now);Save();Render();}}}
    void SalarySettings(){using(var d=new SalaryForm(cfg)){d.StartPosition=FormStartPosition.CenterScreen;d.TopMost=true;if(d.ShowDialog()==DialogResult.OK){cfg.MonthlySalary=d.MonthlySalary;cfg.SalarySet=true;Save();clickThought.Page=1;clickThought.Until=DateTime.Now.AddSeconds(3);showProgressThought=true;showEarningsCelebration=false;previewUntil=DateTime.MinValue;messageUntil=clickThought.Until;ResizePet();}}}
}
public class SalaryForm:Form {
    readonly NumericUpDown amount=new NumericUpDown();
    public decimal MonthlySalary;
    public SalaryForm(CatSettings cfg):this(cfg,0){}
    public SalaryForm(CatSettings cfg,float requestedScale){
        float dpi=requestedScale;if(dpi<=0)using(var g=Graphics.FromHwnd(IntPtr.Zero))dpi=g.DpiX/96f;
        Func<int,int> px=n=>(int)Math.Round(n*dpi);
        AutoScaleMode=AutoScaleMode.None;Font=new Font("Microsoft YaHei UI",12*dpi,FontStyle.Regular,GraphicsUnit.Pixel);
        Text="猫猫 · 工资设置";FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
        BackColor=Color.FromArgb(255,250,239);ForeColor=Color.FromArgb(91,70,60);
        ClientSize=new Size(px(400),px(300));MinimumSize=new Size(Width,1);AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
        var layout=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,Padding=new Padding(px(22)),Margin=Padding.Empty};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        var heading=new Label{Text="今天又赚到罐罐钱了！",AutoSize=true,Font=new Font(Font.FontFamily,18*dpi,FontStyle.Bold,GraphicsUnit.Pixel),Margin=new Padding(0,0,0,px(18))};layout.Controls.Add(heading,0,0);layout.SetColumnSpan(heading,2);
        var salaryLabel=new Label{Text="我的月薪（元）",AutoSize=true,Margin=new Padding(0,0,0,px(7))};layout.Controls.Add(salaryLabel,0,1);layout.SetColumnSpan(salaryLabel,2);
        amount.Maximum=100000000m;amount.Minimum=0;amount.DecimalPlaces=2;amount.ThousandsSeparator=true;amount.Increment=100m;amount.Value=cfg.MonthlySalary;amount.Dock=DockStyle.Fill;amount.Font=new Font(Font.FontFamily,16*dpi,FontStyle.Regular,GraphicsUnit.Pixel);amount.Margin=new Padding(0,0,0,px(18));amount.BackColor=Color.FromArgb(255,253,246);amount.ForeColor=ForeColor;layout.Controls.Add(amount,0,2);layout.SetColumnSpan(amount,2);
        var formula=new Label{Text="每周 "+SalaryMath.WeeklyHours(cfg).ToString("0.##")+" 小时，每月约 "+SalaryMath.MonthlyHours(cfg).ToString("0.##")+" 小时\n月工时按「周总工时 ÷ 7 × 30」折算",AutoSize=true,MaximumSize=new Size(px(356),0),Margin=new Padding(0,0,0,px(9))};layout.Controls.Add(formula,0,3);layout.SetColumnSpan(formula,2);
        var note=new Label{Text="时薪用月薪除以估算月工时。今日收入按当天已工作时长累计，包含午休，下班后停止。设置仅保存在本机。",AutoSize=true,MaximumSize=new Size(px(356),0),Margin=new Padding(0,0,0,px(18))};layout.Controls.Add(note,0,4);layout.SetColumnSpan(note,2);
        var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty};
        var save=new Button{Text="保存",AutoSize=true,MinimumSize=new Size(px(82),px(34)),Padding=new Padding(px(10),px(4),px(10),px(4)),Margin=new Padding(px(8),0,0,0)};save.Click+=(o,e)=>{MonthlySalary=amount.Value;DialogResult=DialogResult.OK;};
        var cancel=new Button{Text="取消",AutoSize=true,MinimumSize=new Size(px(82),px(34)),Padding=new Padding(px(10),px(4),px(10),px(4)),Margin=Padding.Empty,DialogResult=DialogResult.Cancel};
        foreach(var button in new[]{save,cancel}){button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Color.FromArgb(220,206,179);button.FlatAppearance.MouseOverBackColor=Color.FromArgb(238,226,200);button.BackColor=Color.FromArgb(248,239,219);button.UseVisualStyleBackColor=false;}
        actions.Controls.Add(save);actions.Controls.Add(cancel);layout.Controls.Add(actions,0,5);layout.SetColumnSpan(actions,2);Controls.Add(layout);AcceptButton=save;CancelButton=cancel;
    }
}
public class AdvancedScheduleForm:Form {
    public bool EnabledSchedule;
    public DayPlan[] Plans;
    public AdvancedScheduleForm(CatSettings cfg):this(cfg,0){}
    public AdvancedScheduleForm(CatSettings cfg,float requestedScale){
        float dpi=requestedScale;if(dpi<=0)using(var g=Graphics.FromHwnd(IntPtr.Zero))dpi=g.DpiX/96f;
        Func<int,int> px=n=>(int)Math.Round(n*dpi);
        AutoScaleMode=AutoScaleMode.None;Font=new Font("Microsoft YaHei UI",12*dpi,FontStyle.Regular,GraphicsUnit.Pixel);
        Text="猫猫 · 高级作息设置";FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
        BackColor=Color.FromArgb(255,250,239);ForeColor=Color.FromArgb(91,70,60);ClientSize=new Size(px(590),px(360));MinimumSize=new Size(Width,1);AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
        var root=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,Padding=new Padding(px(22)),Margin=Padding.Empty};
        root.Controls.Add(new Label{Text="每天都可以有自己的作息",AutoSize=true,Font=new Font(Font.FontFamily,18*dpi,FontStyle.Bold,GraphicsUnit.Pixel),Margin=new Padding(0,0,0,px(15))});
        var enabled=new CheckBox{Text="启用每天不同的上下班时间",Checked=true,AutoSize=true,Margin=new Padding(0,0,0,px(15))};root.Controls.Add(enabled);
        var grid=new TableLayoutPanel{ColumnCount=5,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Fill,Margin=Padding.Empty};
        foreach(float width in new float[]{19,21,21,21,18})grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,width));
        string[] titles={"工作日","上班","午饭","下班","时长"};for(int c=0;c<5;c++)grid.Controls.Add(new Label{Text=titles[c],AutoSize=true,Margin=new Padding(0,0,0,px(9))},c,0);
        var check=new CheckBox[7];var starts=new DateTimePicker[7];var lunches=new DateTimePicker[7];var ends=new DateTimePicker[7];var hours=new Label[7];
        DayPlan[] initial=cfg.Week==null?cfg.Plans():cfg.Week.Select(p=>p.Copy()).ToArray();
        int[] dayOrder={1,2,3,4,5,6,0};var summary=new Label{AutoSize=true,MaximumSize=new Size(px(546),0),Margin=new Padding(0,px(12),0,px(10))};
        Action update=()=>{decimal total=0;int count=0;for(int i=0;i<7;i++){bool on=check[i].Checked;starts[i].Enabled=lunches[i].Enabled=ends[i].Enabled=on;decimal duration=(decimal)(ends[i].Value.TimeOfDay-starts[i].Value.TimeOfDay).TotalMinutes/60;hours[i].Text=on?(duration>0?duration.ToString("0.##")+" 小时":"待调整"):"休息";if(on&&duration>0){total+=duration;count++;}}summary.Text="每周 "+total.ToString("0.##")+" 小时 · 每月约 "+(total/7m*30m).ToString("0.##")+" 小时\n月工时 ≈ 周总工时 ÷ 7 × 30，包含午休。";};
        for(int i=0;i<7;i++){
            int day=dayOrder[i];var plan=initial.First(p=>p.Day==day);
            check[i]=new CheckBox{Text="周"+new[]{"日","一","二","三","四","五","六"}[day],Checked=plan.Working,AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,0,px(7),px(9))};grid.Controls.Add(check[i],0,i+1);
            DateTimePicker[] picks={new DateTimePicker(),new DateTimePicker(),new DateTimePicker()};int[] values={plan.Start,plan.Lunch,plan.End};
            for(int c=0;c<3;c++){var pick=picks[c];pick.Format=DateTimePickerFormat.Custom;pick.CustomFormat="HH:mm";pick.ShowUpDown=true;pick.Value=DateTime.Today.AddMinutes(values[c]);pick.Width=px(85);pick.MinimumSize=new Size(px(70),0);pick.Dock=DockStyle.Fill;pick.Margin=new Padding(0,0,px(10),px(9));grid.Controls.Add(pick,c+1,i+1);}
            starts[i]=picks[0];lunches[i]=picks[1];ends[i]=picks[2];hours[i]=new Label{AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,0,0,px(9))};grid.Controls.Add(hours[i],4,i+1);
        }
        foreach(var field in starts.Concat(lunches).Concat(ends))field.ValueChanged+=(o,e)=>update();foreach(var field in check)field.CheckedChanged+=(o,e)=>update();update();
        enabled.CheckedChanged+=(o,e)=>{grid.Enabled=enabled.Checked;summary.Enabled=enabled.Checked;};root.Controls.Add(grid);root.Controls.Add(summary);
        var error=new Label{AutoSize=true,ForeColor=Color.Maroon,MinimumSize=new Size(0,px(20)),Margin=new Padding(0,0,0,px(8))};root.Controls.Add(error);
        var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty};
        var save=new Button{Text="使用此排班",AutoSize=true,MinimumSize=new Size(px(100),px(34)),Margin=new Padding(px(8),0,0,0)};
        save.Click+=(o,e)=>{var plans=new DayPlan[7];for(int i=0;i<7;i++){plans[i]=new DayPlan{Day=dayOrder[i],Working=check[i].Checked,Start=starts[i].Value.Hour*60+starts[i].Value.Minute,Lunch=lunches[i].Value.Hour*60+lunches[i].Value.Minute,End=ends[i].Value.Hour*60+ends[i].Value.Minute};if(enabled.Checked&&!plans[i].Valid()){error.Text=check[i].Text+"：请按上班、午饭、下班的顺序设置。";return;}}if(enabled.Checked&&!plans.Any(p=>p.Working)){error.Text="至少选择一个工作日。";return;}EnabledSchedule=enabled.Checked;Plans=plans;DialogResult=DialogResult.OK;};
        var cancel=new Button{Text="取消",AutoSize=true,MinimumSize=new Size(px(82),px(34)),DialogResult=DialogResult.Cancel,Margin=Padding.Empty};
        foreach(var button in new[]{save,cancel}){button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Color.FromArgb(220,206,179);button.FlatAppearance.MouseOverBackColor=Color.FromArgb(238,226,200);button.BackColor=Color.FromArgb(248,239,219);button.UseVisualStyleBackColor=false;}
        actions.Controls.Add(save);actions.Controls.Add(cancel);root.Controls.Add(actions);Controls.Add(root);AcceptButton=save;CancelButton=cancel;
    }
}
public class SettingsForm:Form {
    DateTimePicker start=new DateTimePicker(),lunch=new DateTimePicker(),end=new DateTimePicker();CheckBox[] days=new CheckBox[7];Label error=new Label();
    public int Start,Lunch,End;public int[] Days;
    public bool AdvancedSchedule;public DayPlan[] Week;bool weekCustomized;
    public bool WeekConfigured {get{return weekCustomized;}}
    public SettingsForm(CatSettings s):this(s,0){}
    public SettingsForm(CatSettings s,float requestedScale){
        weekCustomized=s.AdvancedSchedule||s.WeekConfigured;AdvancedSchedule=s.AdvancedSchedule;Week=s.Week==null?s.Plans():s.Week.Select(p=>p.Copy()).ToArray();
        float dpi=requestedScale;
        if(dpi<=0)using(var g=Graphics.FromHwnd(IntPtr.Zero))dpi=g.DpiX/96f;
        Func<int,int> px=n=>(int)Math.Round(n*dpi);
        AutoScaleMode=AutoScaleMode.None;
        Font=new Font("Microsoft YaHei UI",12*dpi,FontStyle.Regular,GraphicsUnit.Pixel);
        Text="猫猫 · 时间设置";FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
        BackColor=Color.FromArgb(255,250,239);ForeColor=Color.FromArgb(91,70,60);
        ClientSize=new Size(px(380),px(300));MinimumSize=new Size(Width,1);AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
        var layout=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,Padding=new Padding(px(22)),Margin=Padding.Empty};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        var header=new Label{Text="几点可以回家呀？",AutoSize=true,Font=new Font(Font.FontFamily,18*dpi,FontStyle.Bold,GraphicsUnit.Pixel),Margin=new Padding(0,0,0,px(18))};
        layout.Controls.Add(header,0,0);layout.SetColumnSpan(header,2);
        layout.Controls.Add(new Label{Text="上班时间",AutoSize=true,Margin=new Padding(0,0,0,px(7))},0,1);
        layout.Controls.Add(new Label{Text="下班时间",AutoSize=true,Margin=new Padding(px(8),0,0,px(7))},1,1);
        start.Format=end.Format=DateTimePickerFormat.Custom;start.CustomFormat=end.CustomFormat="HH:mm";start.ShowUpDown=end.ShowUpDown=true;
        start.Value=DateTime.Today.AddMinutes(s.Start);end.Value=DateTime.Today.AddMinutes(s.End);
        start.Dock=end.Dock=DockStyle.Fill;start.Margin=new Padding(0,0,px(8),px(18));end.Margin=new Padding(px(8),0,0,px(18));
        layout.Controls.Add(start,0,2);layout.Controls.Add(end,1,2);
        var daysLabel=new Label{Text="工作日",AutoSize=true,Margin=new Padding(0,0,0,px(6))};layout.Controls.Add(daysLabel,0,3);layout.SetColumnSpan(daysLabel,2);
        var daysPanel=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Fill,WrapContents=true,Margin=new Padding(0),Padding=new Padding(0)};
        for(int i=0;i<7;i++){int day=(i+1)%7;days[i]=new CheckBox{Text=new string[]{"日","一","二","三","四","五","六"}[day],Tag=day,Checked=s.Days.Contains(day),AutoSize=true,Margin=new Padding(0,0,px(11),px(8))};daysPanel.Controls.Add(days[i]);}
        layout.Controls.Add(daysPanel,0,4);layout.SetColumnSpan(daysPanel,2);
        var lunchLabel=new Label{Text="午饭时间",AutoSize=true,Margin=new Padding(0,px(5),0,px(7))};layout.Controls.Add(lunchLabel,0,5);layout.SetColumnSpan(lunchLabel,2);
        lunch.Format=DateTimePickerFormat.Custom;lunch.CustomFormat="HH:mm";lunch.ShowUpDown=true;lunch.Value=DateTime.Today.AddMinutes(s.Lunch);lunch.Dock=DockStyle.Fill;lunch.Margin=new Padding(0,0,px(8),px(14));layout.Controls.Add(lunch,0,6);
        var note=new Label{Text="到点自动庆祝",AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(px(8),0,0,px(14))};layout.Controls.Add(note,1,6);
        error.AutoSize=true;error.MinimumSize=new Size(0,px(20));error.Margin=new Padding(0,0,0,px(8));error.ForeColor=Color.Maroon;layout.Controls.Add(error,0,7);layout.SetColumnSpan(error,2);
        var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty};
        var save=new Button{Text="保存",AutoSize=true,MinimumSize=new Size(px(82),px(34)),Padding=new Padding(px(10),px(4),px(10),px(4)),Margin=new Padding(px(8),0,0,0)};
        save.Click+=(o,e)=>{Start=start.Value.Hour*60+start.Value.Minute;End=end.Value.Hour*60+end.Value.Minute;Lunch=lunch.Value.Hour*60+lunch.Value.Minute;Days=days.Where(x=>x.Checked).Select(x=>(int)x.Tag).ToArray();if(!AdvancedSchedule&&Start>=End){error.Text="下班时间要晚于上班时间。";return;}if(!AdvancedSchedule&&(Lunch<=Start||Lunch>=End)){error.Text="午饭时间要在上班和下班之间。";return;}if(!AdvancedSchedule&&Days.Length==0){error.Text="至少选一个工作日。";return;}if(AdvancedSchedule){var first=Week.First(p=>p.Working);Start=first.Start;Lunch=first.Lunch;End=first.End;Days=Week.Where(p=>p.Working).Select(p=>p.Day).ToArray();}DialogResult=DialogResult.OK;};
        var cancel=new Button{Text="取消",AutoSize=true,MinimumSize=new Size(px(82),px(34)),Padding=new Padding(px(10),px(4),px(10),px(4)),Margin=Padding.Empty,DialogResult=DialogResult.Cancel};
        var advanced=new Button{Text="高级设置…",AutoSize=true,MinimumSize=new Size(px(100),px(34)),Padding=new Padding(px(7),px(4),px(7),px(4)),Margin=new Padding(0,0,px(8),0)};
        Action refresh=()=>{start.Enabled=end.Enabled=lunch.Enabled=daysPanel.Enabled=!AdvancedSchedule;note.Text=AdvancedSchedule?"已启用逐日排班":"到点自动庆祝";advanced.Text=AdvancedSchedule?"高级设置 · 已开":"高级设置…";};
        advanced.Click+=(o,e)=>{CatSettings draft=new CatSettings{Start=start.Value.Hour*60+start.Value.Minute,Lunch=lunch.Value.Hour*60+lunch.Value.Minute,End=end.Value.Hour*60+end.Value.Minute,Days=days.Where(x=>x.Checked).Select(x=>(int)x.Tag).ToArray(),AdvancedSchedule=AdvancedSchedule,Week=weekCustomized?Week:null};
            using(var dialog=new AdvancedScheduleForm(draft)){dialog.StartPosition=FormStartPosition.CenterParent;if(dialog.ShowDialog(this)==DialogResult.OK){AdvancedSchedule=dialog.EnabledSchedule;Week=dialog.Plans;weekCustomized=true;refresh();}}};refresh();
        advanced.Anchor=AnchorStyles.Left;advanced.Margin=new Padding(0,0,0,px(12));layout.Controls.Add(advanced,0,8);layout.SetColumnSpan(advanced,2);actions.Controls.Add(save);actions.Controls.Add(cancel);layout.Controls.Add(actions,0,9);layout.SetColumnSpan(actions,2);
        foreach(var button in new[]{save,cancel,advanced}){button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Color.FromArgb(220,206,179);button.FlatAppearance.MouseOverBackColor=Color.FromArgb(238,226,200);button.BackColor=Color.FromArgb(248,239,219);button.UseVisualStyleBackColor=false;}
        Controls.Add(layout);AcceptButton=save;CancelButton=cancel;
    }
}
static class Native {
    [StructLayout(LayoutKind.Sequential)] struct POINT{public int x,y;public POINT(int a,int b){x=a;y=b;}}
    [StructLayout(LayoutKind.Sequential)] struct SIZE{public int cx,cy;public SIZE(int x,int y){cx=x;cy=y;}}
    [StructLayout(LayoutKind.Sequential,Pack=1)] struct BLEND{public byte Op,Flags,Alpha,Format;}
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr dst,ref POINT pos,ref SIZE size,IntPtr src,ref POINT origin,int key,ref BLEND blend,int flags);
    public static void Apply(IntPtr hwnd,Bitmap bmp,Point location){
        IntPtr screen=GetDC(IntPtr.Zero),dc=CreateCompatibleDC(screen),hb=IntPtr.Zero,old=IntPtr.Zero;
        try{hb=bmp.GetHbitmap(Color.FromArgb(0));old=SelectObject(dc,hb);var p=new POINT(location.X,location.Y);var zero=new POINT(0,0);var sz=new SIZE(bmp.Width,bmp.Height);var blend=new BLEND{Alpha=255,Format=1};UpdateLayeredWindow(hwnd,screen,ref p,ref sz,dc,ref zero,0,ref blend,2);}
        finally{if(old!=IntPtr.Zero)SelectObject(dc,old);if(hb!=IntPtr.Zero)DeleteObject(hb);DeleteDC(dc);ReleaseDC(IntPtr.Zero,screen);}
    }
}
