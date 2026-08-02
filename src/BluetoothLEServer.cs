using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

#if NETFX
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
#endif

namespace gInk
{
    /// <summary>
    /// BLE GATT server using the Nordic UART Service (NUS) profile.
    /// The phone acts as GATT client and writes touch data to the TX characteristic.
    /// The PC notifies back via the RX characteristic (bidirectional sync).
    /// </summary>
    public class BluetoothLEServer
    {
        public static Root Root;
        private bool running = false;
        private object lockObj = new object();

#if NETFX
        private GattServiceProvider serviceProvider;
        private GattLocalCharacteristic txCharacteristic; // phone → PC (write)
        private GattLocalCharacteristic rxCharacteristic; // PC → phone (notify)
#endif

        private List<MobileSession> sessions = new List<MobileSession>();

        // Nordic UART Service (NUS) UUIDs — standard for BLE serial
        private static readonly Guid SERVICE_UUID = new Guid("6e400001-b5a3-f007-9439-0803f44f0770");
        private static readonly Guid TX_CHAR_UUID = new Guid("6e400003-b5a3-f007-9439-0803f44f0770"); // Write (phone → PC)
        private static readonly Guid RX_CHAR_UUID = new Guid("6e400002-b5a3-f007-9439-0803f44f0770"); // Notify (PC → phone)

        public BluetoothLEServer(Root root) { Root = root; }

        public bool Start(string deviceName)
        {
            try
            {
#if NETFX
                serviceProvider = GattServiceProvider.Create(SERVICE_UUID);

                // TX characteristic: phone writes touch data to PC
                var txParams = new GattLocalCharacteristicParameters();
                txParams.CharacteristicProperties =
                    GattCharacteristicProperties.WriteWithoutResponse;
                txParams.CharacteristicUuid = TX_CHAR_UUID;
                txParams.WriteProtectionLevel = GattSecurityLevel.Encrypted;

                var txResult = serviceProvider.AddCharacteristic(txParams);
                if (txResult.Characteristic != null)
                {
                    txCharacteristic = txResult.Characteristic;
                    txCharacteristic.WriteRequested += OnTxWritten;
                }
                else
                {
                    Console.WriteLine("BLE TX characteristic creation failed");
                    return false;
                }

                // RX characteristic: PC notifies phone (for bidirectional sync - display updates)
                var rxParams = new GattLocalCharacteristicParameters();
                rxParams.CharacteristicProperties =
                    GattCharacteristicProperties.Notify | GattCharacteristicProperties.Read;
                rxParams.CharacteristicUuid = RX_CHAR_UUID;
                rxParams.ReadProtectionLevel = GattSecurityLevel.Encrypted;
                rxParams.WriteProtectionLevel = GattSecurityLevel.Encrypted;
                rxParams.ExtendedProperties = true;

                var rxResult = serviceProvider.AddCharacteristic(rxParams);
                if (rxResult.Characteristic == null)
                {
                    Console.WriteLine("BLE RX characteristic creation failed");
                    return false;
                }
                rxCharacteristic = rxResult.Characteristic;

                serviceProvider.StartAdvertising();
                running = true;
                Console.WriteLine($"BLE GATT server started. Service: {SERVICE_UUID}");
                return true;
#else
                Console.WriteLine("BLE server not supported on this platform");
                return false;
#endif
            }
            catch (Exception ex)
            {
                Console.WriteLine("BLE server start error: " + ex);
                return false;
            }
        }

#if NETFX
        private async void OnTxWritten(GattLocalCharacteristic sender, GattValueRequestedEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                var data = args.Value;
                byte[] frame = data.ToArray();
                if (frame.Length > 0)
                {
                    lock (lockObj)
                    {
                        var sess = sessions.FirstOrDefault() ?? new MobileSession();
                        if (!sessions.Contains(sess)) sessions.Add(sess);
                        sess.LastActivity = DateTime.Now;
                        MobileInputHandler.Instance?.ProcessFrame(sess, frame);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("BLE frame processing error: " + ex);
            }
            finally
            {
                deferral.Complete();
            }
        }
#endif

        /// <summary>
        /// Send data from PC to phone (for bidirectional sync of handwritten content)
        /// </summary>
        public void NotifyPhone(byte[] data)
        {
            if (!running) return;
#if NETFX
            try
            {
                if (rxCharacteristic != null && data != null && data.Length > 0)
                {
                    var writer = new Windows.Storage.Streams.DataWriter();
                    writer.WriteBytes(data);
                    rxCharacteristic.NotifyValue(reader => { }, writer.DetachBuffer());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("BLE notify error: " + ex);
            }
#endif
        }

        public void Stop()
        {
            running = false;
#if NETFX
            try
            {
                serviceProvider?.StopAdvertising();
            }
            catch { }
            try
            {
                if (txCharacteristic != null)
                    txCharacteristic.WriteRequested -= OnTxWritten;
            }
            catch { }
            txCharacteristic = null;
            rxCharacteristic = null;
            serviceProvider = null;
#endif
            lock (sessions) sessions.Clear();
        }

        public void Close() => Stop();

        public bool IsRunning => running;
    }
}
