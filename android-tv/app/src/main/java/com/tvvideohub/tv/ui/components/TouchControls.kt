@file:OptIn(androidx.tv.material3.ExperimentalTvMaterial3Api::class)

package com.tvvideohub.tv.ui.components

import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.RowScope
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.pointer.pointerInput
import androidx.tv.material3.Button

/**
 * tv-material3's clickable Surfaces (Button, Card, …) handle **D-pad ENTER + focus only** — their
 * click path is built on `handleDPadEnter`, with NO `pointerInput`/touch handling — so on a phone
 * every tv button/card is dead to taps (you can see them, but tapping does nothing).
 *
 * [tapClickable] adds a touch-only tap path that fires [onClick], leaving the component's existing
 * D-pad path untouched. Touch taps and D-pad ENTER come from different input sources, so there's no
 * double-firing. We use [detectTapGestures] (pointer-only) on purpose, NOT `Modifier.clickable` —
 * `clickable` would ALSO react to the ENTER key and double-fire alongside the tv component's own
 * `onClick`. Drags (e.g. scrolling a grid of cards) are not taps, so they still pass through.
 */
fun Modifier.tapClickable(enabled: Boolean = true, onClick: () -> Unit): Modifier =
    if (!enabled) this else pointerInput(onClick) { detectTapGestures { onClick() } }

/**
 * Touch tap + long-press path for tv-material3 Surfaces/Cards (which handle the D-pad only). A long
 * hold fires [onLongPress]; a quick tap fires [onClick] — [detectTapGestures] makes the two mutually
 * exclusive, so there's no double-fire. Pair this with a tv [androidx.tv.material3.Card]'s own
 * `onClick`/`onLongClick` (those cover the D-pad ENTER short/long press); touch and D-pad come from
 * different input sources, so the two paths never both fire for one gesture — same reasoning as
 * [tapClickable].
 */
fun Modifier.tapOrLongPressClickable(
    enabled: Boolean = true,
    onLongPress: () -> Unit,
    onClick: () -> Unit,
): Modifier =
    if (!enabled) this
    else pointerInput(onClick, onLongPress) {
        detectTapGestures(onLongPress = { onLongPress() }, onTap = { onClick() })
    }

/**
 * Drop-in replacement for tv-material3 [Button] that ALSO responds to touch taps, so the same UI
 * works on phones (touch) and TVs (D-pad). Visuals/focus behaviour are the tv [Button]'s; this only
 * adds the missing tap handling.
 */
@Composable
fun AppButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    content: @Composable RowScope.() -> Unit,
) {
    Button(
        onClick = onClick,
        enabled = enabled,
        modifier = modifier.tapClickable(enabled, onClick),
        content = content,
    )
}
