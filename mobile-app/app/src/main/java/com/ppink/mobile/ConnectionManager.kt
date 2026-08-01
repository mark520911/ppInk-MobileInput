package com.ppink.mobile

import android.util.Log
import kotlinx.coroutines.*
import java.nio.ByteBuffer

/**
 * Manages the active connection to the ppInk server.
 * Supports WebSocket (WiFi/USB) and BLE transports.
 * Automatically handles reconnection and message routing.
 */
class ConnectionManager(
    private val onConnected: () -> Unit,
    private val onDisconnected: () -> Unit,
    private val onStrokeData: (FrameProtocol.StrokeData) -> Unit,
    private val onError: (String) -> Unit
) {
    companion object {
        private const val TAG = "ConnectionManager"
    }

    private var webSocketClient: WebSocketClient? = null
    private var bleClient: BleClient? = null
    private var connectionType: String = "WIFI"
    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())

    /** Connect via WebSocket (WiFi or USB) */
    fun connectWebSocket(url: String, password: String) {
        scope.launch {
            try {
                connectionType = if (url.contains("localhost")) "USB" else "WIFI"
                webSocketClient = WebSocketClient(url, password,
                    onMessage = { data -> handleIncomingData(data) },
                    onOpen = { onConnected() },
                    onClose = { onDisconnected() },
                    onError = { msg -> onError(msg) }
                )
                webSocketClient?.connect()
            } catch (e: Exception) {
                onError("WebSocket connection failed: ${e.message}")
            }
        }
    }

    /** Connect via BLE */
    fun connectBle(deviceName: String) {
        scope.launch {
            try {
                connectionType = "BLUETOOTH"
                bleClient = BleClient(deviceName,
                    onConnected = { onConnected() },
                    onDisconnected = { onDisconnected() },
                    onDataReceived = { data -> handleIncomingData(data) },
                    onError = { msg -> onError(msg) }
                )
                bleClient?.connect()
            } catch (e: Exception) {
                onError("BLE connection failed: ${e.message}")
            }
        }
    }

    /** Start as BLE server (peripheral mode) */
    fun startBleServer(serviceName: String): Boolean {
        return try {
            connectionType = "BLUETOOTH"
            val server = BleServer(serviceName)
            server.start(
                onClientConnected = { onConnected() },
                onClientDisconnected = { onDisconnected() },
                onDataReceived = { data -> handleIncomingData(data) }
            )
            true
        } catch (e: Exception) {
            onError("BLE server start failed: ${e.message}")
            false
        }
    }

    /** Stop current connection */
    fun disconnect() {
        scope.launch {
            try {
                webSocketClient?.close()
                bleClient?.disconnect()
            } catch (e: Exception) {
                Log.e(TAG, "Disconnect error: ${e.message}")
            }
        }
    }

    /** Send a frame to the PC */
    fun sendFrame(frame: ByteArray) {
        scope.launch {
            try {
                when (connectionType) {
                    "WIFI", "USB" -> webSocketClient?.send(frame)
                    "BLUETOOTH" -> bleClient?.send(frame)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Send error: ${e.message}")
            }
        }
    }

    /** Send a touch event */
    fun sendTouchEvent(msgType: Int, x: Float, y: Float) {
        sendFrame(FrameProtocol.createTouchEvent(msgType, x, y))
    }

    /** Send mode switch */
    fun sendModeSwitch(mode: Int) {
        sendFrame(FrameProtocol.createModeSwitch(mode))
    }

    private fun handleIncomingData(data: ByteArray) {
        try {
            val strokeData = FrameProtocol.parseStrokeData(data)
            if (strokeData != null) {
                onStrokeData(strokeData)
            }
        } catch (e: Exception) {
            Log.e(TAG, "Data parse error: ${e.message}")
        }
    }

    fun cleanup() {
        scope.cancel()
        webSocketClient?.close()
        bleClient?.disconnect()
    }
}
