package com.ppink.mobile

import android.util.Log
import okhttp3.*
import okio.ByteString
import java.util.concurrent.TimeUnit
import okhttp3.HttpUrl.Companion.toHttpUrl

/**
 * WebSocket client for communicating with ppInk PC server.
 * Used for both WiFi (network IP) and USB (ADB reverse localhost) connections.
 */
class WebSocketClient(
    private val serverUrl: String,
    private val password: String,
    private val onMessage: (ByteArray) -> Unit,
    private val onOpen: () -> Unit,
    private val onClose: () -> Unit,
    private val onError: (String) -> Unit
) {
    companion object {
        private const val TAG = "WebSocketClient"
        private const val MSG_FRAME = 0x01
        private const val MSG_PING = 0x04
    }

    private var client: OkHttpClient? = null
    private var webSocket: WebSocket? = null

    fun connect() {
        try {
            var url = serverUrl.toHttpsUrl()
            // Add password as query parameter (server checks QueryString["pwd"])
            if (password.isNotEmpty() && url.queryParameter("pwd") == null) {
                url = url.newBuilder()
                    .addQueryParameter("pwd", password)
                    .build()
            }
            val request = Request.Builder()
                .url(url)
                .addHeader("Sec-WebSocket-Protocol", "ppink")
                .build()

            client = OkHttpClient.Builder()
                .connectTimeout(10, TimeUnit.SECONDS)
                .readTimeout(0, TimeUnit.MILLISECONDS)
                .build()

            webSocket = client?.newWebSocket(request, object : WebSocketListener() {
                override fun onOpen(ws: WebSocket, response: Response) {
                    Log.d(TAG, "WebSocket connected")
                    onOpen()
                }

                override fun onMessage(ws: WebSocket, bytes: ByteString) {
                    val data = bytes.toByteArray()
                    onMessage(data)
                }

                override fun onClosing(ws: WebSocket, code: Int, reason: String) {
                    Log.d(TAG, "WebSocket closing: $code / $reason")
                }

                override fun onClosed(ws: WebSocket, code: Int, reason: String) {
                    Log.d(TAG, "WebSocket closed: $code / $reason")
                    onClose()
                }

                override fun onFailure(ws: WebSocket, t: Throwable, response: Response?) {
                    Log.e(TAG, "WebSocket failure: ${t.message}")
                    onError(t.message ?: "Connection failed")
                }
            })
        } catch (e: Exception) {
            Log.e(TAG, "Connection error: ${e.message}")
            onError(e.message ?: "Connection failed")
        }
    }

    fun send(data: ByteArray) {
        try {
            webSocket?.send(ByteString.of(*data))
        } catch (e: Exception) {
            Log.e(TAG, "Send failed: ${e.message}")
        }
    }

    fun close() {
        try {
            webSocket?.close(1000, "Normal closure")
            client?.dispatcher?.executorService?.shutdownNow()
        } catch (e: Exception) {
            Log.e(TAG, "Close error: ${e.message}")
        }
    }

    private fun String.toHttpsUrl(): HttpUrl {
        var s = this
        if (!s.startsWith("http://") && !s.startsWith("https://"))
            s = "ws://$s"
        s = s.replace("ws://", "http://").replace("wss://", "https://")
        return try {
            s.toHttpUrl()
        } catch (e: Exception) {
            throw IllegalArgumentException("Invalid URL: $s")
        }
    }
}
