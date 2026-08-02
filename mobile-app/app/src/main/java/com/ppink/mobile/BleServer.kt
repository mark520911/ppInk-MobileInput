package com.ppink.mobile

import android.bluetooth.*
import android.bluetooth.le.*
import android.content.Context
import android.util.Log
import android.os.ParcelUuid
import java.util.UUID

/**
 * BLE GATT server using Nordic UART Service (NUS).
 * The phone acts as a BLE peripheral; the PC connects as central (client).
 * PC writes touch data to the TX characteristic (from PC's perspective).
 */
class BleServer(private val deviceName: String) {

    companion object {
        private const val TAG = "BleServer"

        // Nordic UART Service UUIDs
        private val SERVICE_UUID = UUID.fromString("6e400001-b5a3-f007-9439-0803f44f0770")
        private val RX_CHAR_UUID = UUID.fromString("6e400002-b5a3-f007-9439-0803f44f0770") // Notify
        private val TX_CHAR_UUID = UUID.fromString("6e400003-b5a3-f007-9439-0803f44f0770") // Write
    }

    private val context: Context = App.instance
    private val bluetoothManager = context.getSystemService(Context.BLUETOOTH_SERVICE) as BluetoothManager
    private val bluetoothAdapter: BluetoothAdapter? = bluetoothManager.adapter
    private var bleServer: BluetoothGattServer? = null

    fun start(
        onClientConnected: () -> Unit,
        onClientDisconnected: () -> Unit,
        onDataReceived: (ByteArray) -> Unit
    ) {
        if (bluetoothAdapter == null || !bluetoothAdapter.isEnabled) {
            Log.e(TAG, "Bluetooth not available")
            return
        }

        val server = bluetoothManager.openGattServer(context, object : BluetoothGattServerCallback() {
            override fun onConnectionStateChange(device: BluetoothDevice, status: Int, newState: Int) {
                when (newState) {
                    BluetoothProfile.STATE_CONNECTED -> {
                        Log.d(TAG, "BLE client connected: ${device.address}")
                        onClientConnected()
                    }
                    BluetoothProfile.STATE_DISCONNECTED -> {
                        Log.d(TAG, "BLE client disconnected: ${device.address}")
                        onClientDisconnected()
                    }
                }
            }

            override fun onCharacteristicWriteRequest(
                device: BluetoothDevice,
                requestId: Int,
                characteristic: BluetoothGattCharacteristic,
                preparedWrite: Boolean,
                responseNeeded: Boolean,
                offset: Int,
                value: ByteArray
            ) {
                if (characteristic.uuid == TX_CHAR_UUID) {
                    Log.d(TAG, "BLE write received: ${value.size} bytes")
                    onDataReceived(value)
                    // Send response
                    if (responseNeeded) {
                        bleServer?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, offset, value)
                    }
                }
            }

            override fun onCharacteristicReadRequest(
                device: BluetoothDevice,
                requestId: Int,
                offset: Int,
                characteristic: BluetoothGattCharacteristic
            ) {
                if (characteristic.uuid == RX_CHAR_UUID) {
                    val charValue = characteristic.value
                    val responseValue = if (offset <= charValue.size) {
                        charValue.copyOfRange(offset, charValue.size)
                    } else {
                        byteArrayOf()
                    }
                    bleServer?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, offset, responseValue)
                }
            }

            override fun onDescriptorWriteRequest(
                device: BluetoothDevice,
                requestId: Int,
                descriptor: BluetoothGattDescriptor,
                preparedWrite: Boolean,
                responseNeeded: Boolean,
                offset: Int,
                value: ByteArray
            ) {
                if (responseNeeded) {
                    bleServer?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, offset, value)
                }
            }
        })

        val service = BluetoothGattService(SERVICE_UUID, BluetoothGattService.SERVICE_TYPE_PRIMARY)

        val txChar = BluetoothGattCharacteristic(
            TX_CHAR_UUID,
            BluetoothGattCharacteristic.PROPERTY_WRITE_NO_RESPONSE,
            BluetoothGattCharacteristic.PERMISSION_WRITE
        )
        txChar.setValue(byteArrayOf(0))
        service.addCharacteristic(txChar)

        val rxChar = BluetoothGattCharacteristic(
            RX_CHAR_UUID,
            BluetoothGattCharacteristic.PROPERTY_NOTIFY or BluetoothGattCharacteristic.PROPERTY_READ,
            BluetoothGattCharacteristic.PERMISSION_READ
        )
        rxChar.setValue(byteArrayOf(0))
        service.addCharacteristic(rxChar)

        bleServer = server
        server.addService(service)

        // Start advertising
        val advertiser = bluetoothAdapter?.bluetoothLeAdvertiser
        if (advertiser != null) {
            val settings = AdvertiseSettings.Builder()
                .setAdvertiseMode(AdvertiseSettings.ADVERTISE_MODE_LOW_LATENCY)
                .setTxPowerLevel(AdvertiseSettings.ADVERTISE_TX_POWER_HIGH)
                .setConnectable(true)
                .build()

            val data = AdvertiseData.Builder()
                .addServiceUuid(ParcelUuid(SERVICE_UUID))
                .setIncludeDeviceName(true)
                .build()

            advertiser.startAdvertising(settings, data, scanSettingsCallback)
        } else {
            Log.e(TAG, "BLE advertiser not available")
        }

        this.bleServer = server
    }

    private val scanSettingsCallback = object : AdvertiseCallback() {
        override fun onStartSuccess(settingsInEffect: AdvertiseSettings) {
            Log.d(TAG, "BLE advertising started")
        }

        override fun onStartFailure(errorCode: Int) {
            Log.e(TAG, "BLE advertising failed: $errorCode")
        }
    }

    fun notifyPhone(data: ByteArray) {
        try {
            val rxChar = bleServer?.getService(SERVICE_UUID)?.getCharacteristic(RX_CHAR_UUID)
            if (rxChar != null) {
                rxChar.value = data
                // Notify all connected devices
                bleServer?.connectedDevices?.forEach { device ->
                    bleServer?.notifyCharacteristicChanged(device, rxChar, false)
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Notify failed: ${e.message}")
        }
    }

    fun stop() {
        try {
            bleServer?.close()
        } catch (e: Exception) {
            Log.e(TAG, "Stop error: ${e.message}")
        }
        val advertiser = bluetoothAdapter?.bluetoothLeAdvertiser
        advertiser?.stopAdvertising(scanSettingsCallback)
    }
}
