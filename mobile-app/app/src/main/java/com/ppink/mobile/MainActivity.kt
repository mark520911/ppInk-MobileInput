package com.ppink.mobile

import android.Manifest
import android.app.Activity
import android.bluetooth.BluetoothAdapter
import android.content.Intent
import java.net.URLDecoder
import android.content.pm.PackageManager
import android.os.Bundle
import android.view.View
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat

/**
 * Main activity for the ppInk Mobile client.
 *
 * Phone functions as an input device:
 * - Touch/drawing input → sends binary frames to PC
 * - Receives stroke data from PC → renders on canvas
 * - Screen shows ONLY handwritten content (no UI chrome)
 * - Supports WiFi (WebSocket), USB (ADB reverse), Bluetooth (BLE NUS)
 * - QR code scanning for quick connection
 */
class MainActivity : AppCompatActivity() {

    companion object {
        private const val REQUEST_ENABLE_BT = 1001
    }

    private lateinit var drawingView: DrawingView
    private lateinit var bleControls: View
    private var connectionManager: ConnectionManager? = null
    private var currentConfig: ConnectionConfig? = null

    private val qrScanLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == Activity.RESULT_OK) {
            val data = result.data
            val configStr = data?.getStringExtra("SCAN_RESULT")
            if (configStr != null) {
                parseAndConnect(configStr)
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Fullscreen immersive mode — phone shows ONLY handwritten content
        window.decorView.systemUiVisibility = (
            View.SYSTEM_UI_FLAG_LAYOUT_STABLE
            or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
            or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
            or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
            or View.SYSTEM_UI_FLAG_FULLSCREEN
            or View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
        )

        setContentView(R.layout.activity_main)

        drawingView = findViewById(R.id.drawingView)
        bleControls = findViewById(R.id.bleControls)
        val btnScanQr = findViewById<View>(R.id.btnScanQr)

        btnScanQr.setOnClickListener {
            openQrScanner()
        }

        // Check permissions
        checkPermissions()

        // Setup connection callback
        connectionManager = ConnectionManager(
            onConnected = {
                runOnUiThread {
                    Toast.makeText(this, "Connected to ppInk", Toast.LENGTH_SHORT).show()
                }
            },
            onDisconnected = {
                runOnUiThread {
                    Toast.makeText(this, "Disconnected from ppInk", Toast.LENGTH_SHORT).show()
                }
            },
            onStrokeData = { strokeData ->
                drawingView.addStrokeFromPc(strokeData)
            },
            onError = { msg ->
                runOnUiThread {
                    Toast.makeText(this, "Error: $msg", Toast.LENGTH_LONG).show()
                }
            }
        )

        // Set up drawing callbacks
        drawingView.onFrameSend = { frame ->
            connectionManager?.sendFrame(frame)
        }
        drawingView.onRequestPcInk = {
            connectionManager?.sendModeSwitch(FrameProtocol.MODE_DRAWING)
        }
    }

    private fun checkPermissions() {
        val needed = mutableListOf<String>()
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA)
            != PackageManager.PERMISSION_GRANTED)
            needed.add(Manifest.permission.CAMERA)
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_FINE_LOCATION)
            != PackageManager.PERMISSION_GRANTED)
            needed.add(Manifest.permission.ACCESS_FINE_LOCATION)
        if (needed.isNotEmpty())
            ActivityCompat.requestPermissions(this, needed.toTypedArray(), 0)
    }

    private fun openQrScanner() {
        try {
            val intent = Intent(this, QRScannerActivity::class.java)
            qrScanLauncher.launch(intent)
        } catch (e: Exception) {
            Toast.makeText(this, "QR scanner not available: ${e.message}", Toast.LENGTH_LONG).show()
        }
    }

    private fun parseAndConnect(configStr: String) {
        try {
            val config = parseConfigString(configStr)
            if (config != null) {
                currentConfig = config
                connect(config)
            } else {
                Toast.makeText(this, "Invalid QR code", Toast.LENGTH_LONG).show()
            }
        } catch (e: Exception) {
            Toast.makeText(this, "Parse error: ${e.message}", Toast.LENGTH_LONG).show()
        }
    }

    private fun parseConfigString(configStr: String): ConnectionConfig? {
        if (!configStr.startsWith("ppink://")) return null

        val rawParams = configStr.substring("ppink://".length).split("&").associate {
            val parts = it.split("=", limit = 2)
            parts[0] to (parts.getOrNull(1) ?: "")
        }

        // URL-decode all parameter values
        val params = rawParams.mapValues { URLDecoder.decode(it.value, "UTF-8") }

        val type = params["type"] ?: return null
        return when (type.uppercase()) {
            "WIFI" -> ConnectionConfig(
                type = "WIFI",
                url = params["url"] ?: return null,
                password = params["pwd"] ?: "",
                mapping = params["mapping"] ?: "FullScreen"
            )
            "USB" -> ConnectionConfig(
                type = "USB",
                url = "ws://localhost:${params["port"] ?: "8080"}/",
                password = params["pwd"] ?: "",
                mapping = params["mapping"] ?: "FullScreen"
            )
            "BLUETOOTH" -> ConnectionConfig(
                type = "BLUETOOTH",
                bleName = params["name"] ?: "ppInk",
                mapping = params["mapping"] ?: "FullScreen"
            )
            else -> null
        }
    }

    private fun connect(config: ConnectionConfig) {
        when (config.type) {
            "WIFI", "USB" -> {
                connectionManager?.connectWebSocket(config.url, config.password)
            }
            "BLUETOOTH" -> {
                connectionManager?.connectBle(config.bleName ?: "ppInk")
            }
        }
    }

    override fun onDestroy() {
        connectionManager?.cleanup()
        super.onDestroy()
    }

    data class ConnectionConfig(
        val type: String,
        val url: String = "",
        val password: String = "",
        val bleName: String? = null,
        val mapping: String = "FullScreen"
    )
}
