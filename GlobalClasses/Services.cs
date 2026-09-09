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
            // Preserve the proven SDK lifetime: CoreApi is created once on the
            // UI thread before connection services begin polling it. This does
            // not contact FEE or load interfaces. Machines without a complete
            // runtime remain usable; the explicit Connect command retries and
            // reports the initialization error there.
            if (ApiInstance is null)
            {
                try
                {
                    ApiInstance = new CoreApi();
                }
                catch
                {
                    ApiInstance = null;
                }
            }

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
        /// Returns the shared FEE SDK client, or retries its construction when
        /// startup could not create it because a runtime dependency was missing.
        /// Interface data is never loaded here.
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
