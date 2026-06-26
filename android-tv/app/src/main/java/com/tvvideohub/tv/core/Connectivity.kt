package com.tvvideohub.tv.core

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities

/** Best-effort check for usable internet (validated transport when the OS reports it). */
fun Context.hasInternet(): Boolean {
    val cm = getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager ?: return false
    val network = cm.activeNetwork ?: return false
    val caps = cm.getNetworkCapabilities(network) ?: return false
    return caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) &&
        caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)
}
