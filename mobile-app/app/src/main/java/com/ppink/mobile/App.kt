package com.ppink.mobile

import android.app.Application

/** Simple Application class for global context access */
class App : Application() {
    companion object {
        lateinit var instance: App
            private set
    }

    override fun onCreate() {
        super.onCreate()
        instance = this
    }
}
