using System.Reflection;
using VIBN_Tools.GlobalClasses;
using VIBN_Tools.GlobalClasses.FeeObjects;
using static VIBN_Tools.GlobalClasses.FeeObjects.FeeLogic;
using static VIBN_Tools.GlobalClasses.Interfaces;

namespace VIBN_Tools.ContainerToFee.GrobStandard
{
    public class GrobBeltControl_Container : ContainerBaseClass, ILogicOwner
    {

        public GrobBeltControl_Container()
        {
            SlotAssignment = new Dictionary<string, PropertyInfo>()
            {
                {LogicsStandard.Grob_BeltControl.Slots.BeltControlOk, typeof(GrobBeltControl_Container).GetProperty("Signal_BeltControlOk") },
                {LogicsStandard.Grob_BeltControl.Slots.BeltControlNok, typeof(GrobBeltControl_Container).GetProperty("Signal_BeltControlNok") },
            };

        }


        public FeeLogic Logic_BeltControl { get; set; }

        public FeeInterfaceSignal Signal_BeltControlOk { get; set; }
        public FeeInterfaceSignal Signal_BeltControlNok { get; set; }

        public bool Parameter_AxisIsRotary { get; set; }
        public float Parameter_ChangeStep_mm { get; set; }








        async Task<FeeLogic> ILogicOwner.CreateLogicAsync(FeeAbstractObject parentObject)
        {
            // Reference right Logic Version and Guid to create LogicObject
            Logic_BeltControl = new FeeLogic()
            {
                Name = ComponentName,
                LogicDefinitionName = LogicsStandard.Grob_BeltControl.Name,
                LogicDefinitionPath = LogicsStandard.Grob_BeltControl.Path,
                Parent = parentObject,
            };

            (Logic_BeltControl.LogicDefinitionGuid, Logic_BeltControl.LogicDefinitionVersion) = await GetOrImportLogicDefinition(Logic_BeltControl.LogicDefinitionName, Logic_BeltControl.LogicDefinitionPath);
            await Logic_BeltControl.CreateSendAssignAndWaitAsync();

            return Logic_BeltControl;
        }


        async Task ILogicOwner.AssignSignalsAsync(FeeInterface targetInterface)
        {
            // Map signals to LogicObject if existing
            var mappings = new (FeeInterfaceSignal Signal, string SlotName)[]
            {
               (Signal_BeltControlOk, LogicsStandard.Grob_BeltControl.Slots.BeltControlOk),
                (Signal_BeltControlNok, LogicsStandard.Grob_BeltControl.Slots.BeltControlNok),
            };

            foreach (var (signal, slotname) in mappings)
            {
                if (signal != null)
                {
                    await signal.CreateSignalAsync(targetInterface);
                    await ContainerSlotLinkService.AssignVariableAndVerifyAsync(
                        Logic_BeltControl.Guid, slotname, signal.Guid,
                        $"BeltControl {ComponentName}: {slotname}");
                }
            }
        }
    }
}
