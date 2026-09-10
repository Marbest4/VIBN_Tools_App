using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Automation;
using FS.SDK.Components;
using FS.SDK.Mathematics;
using FS.SDK.Scene.Objects;
using FS.SDK.Utilities;
using VIBN_Tools.GlobalClasses;
using VIBN_Tools.GlobalClasses.FeeObjects;
using static VIBN_Tools.GlobalClasses.FeeObjects.FeeLogic;
using static VIBN_Tools.GlobalClasses.Interfaces;

namespace VIBN_Tools.ContainerToFee.GrobStandard
{
    public class GrobCylinder_Container : ContainerBaseClass, ISimObjectFindOrSelect, ILogicSimObjectOwner
    {

        public GrobCylinder_Container()
        {
            SlotAssignment = new Dictionary<string, PropertyInfo>()
            {
                {LogicsStandard.Grob_Cylinder.Slots.ToHomePos, typeof(GrobCylinder_Container).GetProperty(nameof(Signal_ToHomePos)) },
                {LogicsStandard.Grob_Cylinder.Slots.ToWorkPos, typeof(GrobCylinder_Container).GetProperty(nameof(Signal_ToWorkPos)) },

                {LogicsStandard.Grob_Cylinder.Slots.InHomePos, typeof(GrobCylinder_Container).GetProperty(nameof(Signals_InHomePos)) },

                {LogicsStandard.Grob_Cylinder.Slots.InWorkPos, typeof(GrobCylinder_Container).GetProperty(nameof(Signals_InWorkPos)) },

                {LogicsStandard.Grob_Cylinder.Slots.ReleaseClamping, typeof(GrobCylinder_Container).GetProperty(nameof(Signal_ReleaseClamping)) },
                {LogicsStandard.Grob_Cylinder.Slots.ClampingReleased, typeof(GrobCylinder_Container).GetProperty(nameof(Signal_ClampingReleased)) },
            };
        }


        public FeeLogic Logic_Cylinder { get; set; }

        public FeeInterfaceSignal Signal_ToHomePos { get; set; }
        public FeeInterfaceSignal Signal_ToWorkPos { get; set; }

        public List<FeeInterfaceSignal> Signals_InHomePos { get; set; }
        public List<FeeInterfaceSignal> Signals_InWorkPos { get; set; }

        public FeeInterfaceSignal Signal_ReleaseClamping { get; set; }
        public FeeInterfaceSignal Signal_ClampingReleased { get; set; }

        public List<FeeJoint> Joints_Cylinder { get; set; } = new List<FeeJoint>();


        public float Parameter_HomePos { get; set; } = -1f;
        public float Parameter_WorkPos { get; set; } = -1f;
        public float Parameter_OperationTime { get; set; } = -1f;

        public bool IsCreationRequested { get; set; }




        void ISimObjectFindOrSelect.FindSimObjects(ObservableCollection<FeeAbstractObject> mappableSimObjects)
        {
            Joints_Cylinder = FindSimObjectsByNameAndType<FeeJoint>(mappableSimObjects);
        }

        IEnumerable<SimObjectTarget> ISimObjectFindOrSelect.GetSimObjectTargets()
        {
            yield return new SimObjectTarget()
            {
                DisplayName = "MotionJoints",

                AllowedType = typeof(FeeJoint),

                AllowMultiSelect = true,

                GetObjects = () => Joints_Cylinder,

                AssignObjects = objects =>
                {
                    Joints_Cylinder = objects.OfType<FeeJoint>().ToList();
                }
            };
        }





        async Task<FeeLogic> ILogicSimObjectOwner.CreateLogicAsync(FeeAbstractObject parentObject)
        {
            Logic_Cylinder = new FeeLogic()
            {
                Name = this.ComponentName,
                LogicDefinitionName = LogicsStandard.Grob_Cylinder.Name,
                LogicDefinitionPath = LogicsStandard.Grob_Cylinder.Path,
                Parent = parentObject,
            };

            (Logic_Cylinder.LogicDefinitionGuinvã_-¢G§²ÚîÆ­yÓQY˜][

NÃBˆ˜\ˆ[\ÜH]ØZ][K”™XYœ›ÛQš[P\Ş[˜Êš[JNÃBˆYˆ
Z[\Ü’\ÔİXØÙ\ÜÊCBˆ›İÈ™]È[˜[YÜ\˜][Û‘^Ù\[ÛŠ	’[\Ü›Ûˆ	ŞÙš[_IÈ™ZÙ\ØÚYÙ[ˆÚ[\Ü‘\œ›Ü“Y\ÜØYÙ_HŠNÃBˆYˆ
[\Ü•˜[YKÛİ[OH
CBˆ›İÈ™]È[˜[YÜ\˜][Û‘^Ù\[ÛŠ	’[\Ü›Ûˆ	ŞÙš[_IÈYY™\HÙZ[™HÚYÛ˜[KˆŠNÃBƒBˆËÈH[\H™\]Z\™[Y[È[Ù[˜[Y]\ÈHÛÛ\]HÖ]ËYÙ[™\˜]ÜƒBˆËÈ[™[Ù™ˆÚ]İ]™][™[™È]Hİ\İÛY\ˆÛÛ\Û™[X\[™È^\İËƒBˆ˜\ˆ™\]Z\™[Y[ÈHØİ[Y[”\œÙJ]]ĞÜ™X]Oš[\“\İÏĞ]]ĞÜ™X]OˆŠNÃBˆ˜\ˆÙ[™\˜]ÜˆH™]ÈÛÛZ[™\‘Ù[™\˜]ÜŠ
NÃBˆ˜\ˆÙ[™\˜][ÛˆH]ØZ]Ù[™\˜]Ü‹‘Ù[™\˜]P\Ş[˜ÊBˆ™]ÈÛÛZ[™\‘Ù[™\˜][Û”™\]Y\İ
Bˆ[\Ü•˜[YKBˆ™\]Z\™[Y[ËBˆ\œ˜^K‘[\OÜ›İ\[™Ô[OŠ
KBˆ[BˆYÛ›Ü™PØ\ÙNˆYKBˆ\ÙQš[\“\İˆ˜[ÙJJNÃBƒBˆYˆ
Ù[™\˜][Û‹”İ]\İXÜË•İ[ÚYÛ˜[ÈOH[\Ü•˜[YKÛİ[BˆÙ[™\˜][Û‹•[˜\ÜÚYÛ™YÚYÛ˜[ËÛİ[OH[\Ü•˜[YKÛİ[
CBˆÃBˆ›İÈ™]È[˜[YÜ\˜][Û‘^Ù\[ÛŠBˆ	‘Ù[™\˜]Ü°ï™\™ØX™H°ïˆ	ŞÙš[_IÈ\İ[šÛÛœÚ\İ[ˆˆ
ÃBˆ	’[\Ü^Ú[\Ü•˜[YKÛİ[Kİ[^ÙÙ[™\˜][Û‹”İ]\İXÜË•İ[ÚYÛ˜[ßKˆ
ÃBˆ	•[˜\ÜÚYÛ™Y^ÙÙ[™\˜][Û‹•[˜\ÜÚYÛ™YÚYÛ˜[ËÛİ[KˆŠNÃBˆCBƒBˆÛÛœÛÛK•Üš]S[™JBˆ	Ô]‘Ù]š[S˜[YJš[J_NˆÚ[\Ü•˜[YKÛİ[HÚYÛ˜[H\™›ÛÜ™ZXÚZ[™Ù[\Ù[ˆ[™™\˜\˜™Z]]ˆŠNÃBˆCBŸCB