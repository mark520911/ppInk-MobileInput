package com.ppink.mobile

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.SurfaceHolder
import android.view.SurfaceView
import android.view.View
import java.nio.ByteBuffer

/**
 * Custom drawing surface for the ppInk mobile client.
 * - Captures touch → touch events and encodes them as binary frames
 * - Renders strokes received from PC (handwritten content only)
 * - Shows ONLY the handwritten content on screen (no UI chrome)
 */
class DrawingView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyle: Int = 0
) : SurfaceView(context, attrs, defStyle), SurfaceHolder.Callback {

    private val paintInk = Paint()
    private val paintCursor = Paint()
    private val path = Path()
    private val strokes = mutableListOf<Stroke>()

    // Current touch state
    private var currentX = 0f
    private var currentY = 0f
    private var isDrawing = false

    // Mode: true = drawing (ink), false = cursor control
    var isCursorMode = false
        set(value) {
            field = value
            invalidate()
        }

    // Connection callbacks
    var onFrameSend: ((ByteArray) -> Unit)? = null
    var onRequestPcInk: (() -> Unit)? = null

    data class Stroke(
        val points: MutableList<Pair<Float, Float>>,
        val color: Int,
        val width: Float
    )

    init {
        paintInk.color = Color.BLACK
        paintInk.style = Paint.Style.STROKE
        paintInk.strokeCap = Paint.Cap.ROUND
        paintInk.strokeJoin = Paint.Join.ROUND
        paintInk.isAntiAlias = true
        paintInk.strokeWidth = 5f

        paintCursor.color = Color.BLUE
        paintCursor.style = Paint.Style.FILL
        paintCursor.strokeWidth = 1f

        holder.addCallback(this)
        setFocusable(true)
        setFocusableInTouchMode(true)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val w = width.toFloat()
        val h = height.toFloat()

        // Background: pure white (phone shows only handwritten content)
        canvas.drawColor(Color.WHITE)

        // Draw all stored strokes
        for (stroke in strokes) {
            paintInk.color = stroke.color
            paintInk.strokeWidth = stroke.width
            stroke.points.forEachIndexed { idx, point ->
                if (idx > 0) {
                    val prev = stroke.points[idx - 1]
                    canvas.drawLine(prev.first * w, prev.second * h,
                        point.first * w, point.second * h, paintInk)
                }
            }
        }

        // Draw current active path
        if (isDrawing) {
            paintInk.color = Color.BLACK
            paintInk.strokeWidth = 5f
            canvas.drawPath(path, paintInk)
        }

        // Draw cursor indicator if in cursor mode
        if (isCursorMode) {
            canvas.drawCircle(currentX * w, currentY * h, 20f, paintCursor)
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        val x = event.x / width.toFloat()  // normalize 0..1
        val y = event.y / height.toFloat()
        currentX = x
        currentY = y

        when (event.action) {
            MotionEvent.ACTION_DOWN -> {
                isDrawing = true
                path.reset()
                path.moveTo(event.x, event.y)
                if (isCursorMode) {
                    onFrameSend?.invoke(FrameProtocol.createTouchEvent(
                        FrameProtocol.MSG_TOUCH_DOWN, x, y))
                } else {
                    onFrameSend?.invoke(FrameProtocol.createTouchEvent(
                        FrameProtocol.MSG_TOUCH_DOWN, x, y))
                    // Request PC to enter inking mode
                    onRequestPcInk?.invoke()
                }
                return true
            }
            MotionEvent.ACTION_MOVE -> {
                if (isDrawing) {
                    path.lineTo(event.x, event.y)
                    onFrameSend?.invoke(FrameProtocol.createTouchEvent(
                        FrameProtocol.MSG_TOUCH_MOVE, x, y))
                }
                return true
            }
            MotionEvent.ACTION_UP -> {
                isDrawing = false
                path.reset()
                onFrameSend?.invoke(FrameProtocol.createTouchEvent(
                    FrameProtocol.MSG_TOUCH_UP, x, y))
                return true
            }
        }
        return super.onTouchEvent(event)
    }

    /** Called by ConnectionManager when PC sends stroke data */
    fun addStrokeFromPc(stroke: FrameProtocol.StrokeData) {
        post {
            val s = Stroke(
                points = stroke.points.toMutableList(),
                color = stroke.color,
                width = stroke.width
            )
            strokes.add(s)
            invalidate()
        }
    }

    /** Clear all strokes */
    fun clear() {
        strokes.clear()
        invalidate()
    }

    // SurfaceHolder.Callback
    override fun surfaceCreated(holder: SurfaceHolder) {
        // Start rendering thread
    }

    override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {}

    override fun surfaceDestroyed(holder: SurfaceHolder) {}
}
