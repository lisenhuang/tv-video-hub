package com.tvvideohub.tv.core

import android.content.Context
import android.content.res.Configuration
import java.util.Locale

/**
 * Applies the user's [AppLanguage] choice by overriding the [Context]'s locale.
 *
 * Activities call [wrap] from `attachBaseContext` so every resource lookup (including
 * `stringResource` / `getString`) resolves against the chosen language:
 *  - [AppLanguage.SYSTEM]  -> return the base context unchanged, so the OS language wins
 *    (a non-Chinese system resolves to the default `values/` resources = English).
 *  - [AppLanguage.ENGLISH] -> [Locale.ENGLISH] (resolves `values/`).
 *  - [AppLanguage.CHINESE] -> [Locale.SIMPLIFIED_CHINESE] (resolves `values-zh`).
 */
object LocaleHelper {

    fun wrap(base: Context, lang: AppLanguage): Context {
        val locale = when (lang) {
            AppLanguage.SYSTEM -> return base
            AppLanguage.ENGLISH -> Locale.ENGLISH
            AppLanguage.CHINESE -> Locale.SIMPLIFIED_CHINESE
        }
        Locale.setDefault(locale)
        val config = Configuration(base.resources.configuration)
        config.setLocale(locale)
        return base.createConfigurationContext(config)
    }
}
