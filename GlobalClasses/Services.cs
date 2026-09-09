using FS.API;
using System.ComponentModel;
using System.Windows;
using VIBN_Tools.GlobalClasses.FeeObjects;
using VIBN_Tools.Settings;

namespace VIBN_Tools.GlobalClasses
{
    public static class Services
    {
        private static readonly object FeeApiInitializationLock = new();

        public static CoreApi ApiInstance { get; private set; }
        public static bool IsFeeApiInitialized => ApiInstance is not null;
        public static FeeConnectionService Connection { get; private set; }
        public static FeeObjectService FeeObjects { get; private set; }
        public static ProjectSettings ProjectSettings { get; } = new ProjectSettings();





        public static void Initialize()
        {
            if (!DesignerProperties.GetIsInDesignMode(new DependencyObject()))
            {
                Connection = new FeeConnectionService();
            }

            FeeObjects = new FeeObjectService();

            // Load Fee Data only once
            //if (Connection != null)
            //{
            //    Action handler = null;

            //    handler = async () =>
            //    {
            //        if (Connection.IsConnected)
            //        {
            //            Connection.Connected -= handler;
            //            await FeeObjects.UpdateFeeDataAsync();
            //        }
            //    };
            //    Connection.Connected += handler;
            //}

            // Load Fee Data on every connect
            if (Connection != null)
            {
                Connection.Connected += async () =>
                {
                    if (Connection.LoadFeeDataOnConnect)
                    {
                        await FeeObjects.GetInitialFeeDataAsync();
                    }
                    
                };
            }


        }

        /// <summary>
        /// Creates the FEE SDK client only after the user explicitly requests a
        /// connection. Constructing CoreApi during application startup can make
        /// the vendor runtime display connection/interface diagnostics although
        /// no FEE feature is being used.
        /// </summary>
        public static bool TryInitializeFeeApi(out Exception exception)
        {
            lock (FeeApiInitializationLock)
            {
                if (ApiInstance is not null)
                {
                    exception = null;
                    return true;
                }

                try
                {
                    ApiInstance = new CoreApi();
                    exception = null;
                    return true;
                }
                catch (Exception initializationException)
                {
                    ApiInstance = null;
                    exception = initializationException;
                    return false;
                }
            }
        }
    }
}
