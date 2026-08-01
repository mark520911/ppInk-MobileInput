package com.ppink.mobile

import java.nio.ByteBuffer

/**
 * Binary frame protocol for communicating with ppInk PC server.
 * Frame format:
 *   - Byte 0: message type
 *     0x01 = TouchDown
 *     0x02 = TouchMove
 *     0x03 = TouchUp
 *     0x07 = ModeSwitch (byte: 0=Drawing, 1=Cursor)
 *   - For touch events: 4 bytes (float) x (normalized 0..1) + 4 bytes (float) y
 *   - For mode switch: 1 byte (mode)
 */
object FrameProtocol {
    const val MSG_TOUCH_DOWN = 0x01
    const val MSG_TOUCH_MOVE = 0x02
    const val MSG_TOUCH_UP = 0x03
    const val MSG_MODE_SWITCH = 0x07

    const val MODE_DRAWING = 0
    const val MODE_CURSOR = 1

    /** Creates a touch event frame. X and Y are normalized (0.0..1.0) */
    fun createTouchEvent(msgType: Int, x: Float, y: Float): ByteArray {
        val buf = ByteBuffer.allocate(1 + 4 + 4)
        buf.put(msgType.toByte())
        buf.putFloat(x)
        buf.putFloat(y)
        return buf.array()
    }

    /** Creates a mode switch frame */
    fun createModeSwitch(mode: Int): ByteArray {
        return byteArrayOf(MSG_MODE_SWITCH, mode.toByte())
    }

    /** Parses an incoming frame from PC (stroke rendering data) */
    data class StrokeData(
        val points: List<Pair<Float, Float>>,
        val color: Int,
        val width: Float
    )

    /**
     * Incoming PC → phone stroke data format:
     *   Byte 0: 0x10 = StrokeData
     *   Byte 1-4: stroke ID (int)
     *   Byte 5-8: color ARGB (int)
     *   Byte 9-12: width (float)
     *   Byte 13: point count (uint8, max 255)
     *   Byte 14+: for each point: 4 bytes (float x) + 4 bytes (float y)
     */
    fun parseStrokeData(frame: ByteArray): StrokeData? {
        if (frame.isEmpty() || frame[0].toInt() != 0x10) return null
        if (frame.size < 14) return null
        try {
            val pointCount = frame[13].toInt()
            val points = mutableListOf<Pair<Float, Float>>()
            val offset = 14
            val expectedSize = offset + pointCount * 8
            if (frame.size < expectedSize) return null
            for (i in 0 until pointCount) {
                val px = ByteBuffer.wrap(frame, offset + i * 8, 4).float
                val py = ByteBuffer.wrap(frame, offset + i * 8 + 4, 4).float
                points.add(Pair(px, py))
            }
            val color = ByteBuffer.wrap(frame, 5, 4).int
            val width = ByteBuffer.wrap(frame, 9, 4).float
            return StrokeData(points, color, width)
        } catch (e: Exception) {
            return null
        }
    }
}
