package com.ppink.mobile

import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothGatt
import android.bluetooth.BluetoothGattCallback
import android.bluetooth.BluetoothGattCharacteristic
import android.bluetooth.BluetoothGattDescriptor
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothProfile
import android.content.Context
import android.util.Log
import java.util.UUID

/**
 * BLE GATT client using Nordic UART Service (NUS).
 * Connects to the ppInk BLE server on the PC and sends/receives data.
 */
class BleClient(
    private val deviceName: String,
    private val onConnected: () -> Unit,
    private val onDisconnected: () -> Unit,
    private val onDataReceived: (ByteArray) -> Unit,
    private val onError: (String) -> Unit
) {
    companion object {
        private const val TAG = "BleClient"

        // Nordic UART Service UUIDs
        private val SERVICE_UUID = UUID.fromString("6e400001-b5a3-f007-9439-0803f44f0770")
        private val TX_CHAR_UUID = UUID.fromString("6e400003-b5a3-f007-9439-0803f44f0770") // Phone writes → PC
        private val RX_CHAR_UUID = UUID.fromString("6e400002-b5a3-f007-9439-0803f44f0770") // PC notifies → Phone

        private const val MAX_PACKET_SIZE = 247 // BLE MTU - 5
    }

    private val context: Context = App.instance
    private val bluetoothManager = context.getSystemService(Context.BLUETOOTH_SERVICE) as BluetoothManager
    private val bluetoothAdapter: BluetoothAdapter? = bluetoothManager.adapter
    private var bleGatt: BluetoothGatt? = null

    private var txChar: BluetoothGattCharacteristic? = null
    private var rxChar: BluetoothGattCharacteristic? = null

    fun connect() {
        if (bluetoothAdapter == null || !bluetoothAdapter.isEnabled) {
            onError("Bluetooth is not available")
            return
        }

        val device: BluetoothDevice? = findDeviceByName(deviceName)
        if (device == null) {
            onError("BLE device '$deviceName' not found")
            return
        }

        bleGatt = device.connectGatt(context, false, gattCallback)
    }

    private fun findDeviceByName(name: String): BluetoothDevice? {
        val paired = bluetoothAdapter?.bondedDevices ?: return null
        return paired.firstOrNull { it.name == name }
    }

    private val gattCallback = object : BluetoothGattCallback() {
        override fun onConnectionStateChange(gatt: BluetoothGatt, status: Int, newState: Int) {
            when (newState) {
                BluetoothProfile.STATE_CONNECTED -> {
                    Log.d(TAG, "BLE connected, discovering services...")
                    gatt.discoverServices()
                }
                BluetoothProfile.STATE_DISCONNECTED -> {
                    Log.d(TAG, "BLE disconnected")
                    cleanup()
                    onDisconnected()
                }
            }
        }

        override fun onServicesDiscovered(gatt: BluetoothGatt, status: Int) {
            if (status != BluetoothGatt.GATT_SUCCESS) return
            val service = gatt.getService(SERVICE_UUID)
            if (service == null) {
                onError("NUS service not found")
                return
            }
            txChar = service.getCharacteristic(TX_CHAR_UUID)
            rxChar = service.getCharacteristic(RX_CHAR_UUID)

            gatt.setCharacteristicNotification(rxChar, true)
            onConnected()
        }

        override fun onCharacteristicChanged(gatt: BluetoothGatt, characteristic: BluetoothGattCharacteristic) {
            if (characteristic.uuid == RX_CHAR_UUID) {
                val data = characteristic.value
                onDataReceived(data)
            }
        }

        override fun onCharacteristicWrite(gatt: BluetoothGatt, characteristic: BluetoothGattCharacteristic, status: Int) {
            if (status != BluetoothGatt.GATT_SUCCESS) {
                Log.e(TAG, "BLE write failed: $status")
            }
        }
    }

    fun send(data: ByteArray) {
        try {
            val chunks = data.toList().chunked(MAX_PACKET_SIZE)
            for (chunk in chunks) {
                val bytes = chunk.toByteArray()
                txChar?.value = bytes
                val writeOk = bleGatt?.writeCharacteristic(txChar)
                if (writeOk != true) {
                    Log.e(TAG, "BLE send failed")
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "BLE send error: ${e.message}")
            onError(e.message ?: "Send failed")
        }
    }

    fun disconnect() {
        cleanup()
        onDisconnected()
    }

    private fun cleanup() {
        bleGatt?.close()
        bleGatt = null
        txChar = null
        rxChar = null
    }
}
