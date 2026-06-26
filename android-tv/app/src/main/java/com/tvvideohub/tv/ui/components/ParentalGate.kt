@file:OptIn(androidx.tv.material3.ExperimentalTvMaterial3Api::class)

package com.tvvideohub.tv.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.tv.material3.Button
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text
import kotlin.random.Random

/**
 * A simple parental gate: the user must solve a two-digit × one-digit multiplication
 * (e.g. 17 × 7) before a protected action proceeds. Designed to stop young children from
 * changing settings, not to be cryptographically secure. Works with D-pad and touch.
 */
@Composable
fun ParentalGate(
    prompt: String = "Enter the answer to continue",
    onPass: () -> Unit,
    onCancel: () -> Unit,
) {
    // Bumping [round] regenerates the problem (used after a wrong answer).
    var round by remember { mutableIntStateOf(0) }
    val a by remember(round) { mutableStateOf(Random.nextInt(11, 100)) } // two digits
    val b by remember(round) { mutableStateOf(Random.nextInt(2, 10)) }   // one digit
    var answer by remember(round) { mutableStateOf("") }
    var wrong by remember { mutableStateOf(false) }
    val colors = MaterialTheme.colorScheme

    Box(
        modifier = Modifier.fillMaxSize().background(Color(0xCC000000)),
        contentAlignment = Alignment.Center
    ) {
        Column(
            modifier = Modifier
                .widthIn(max = 420.dp)
                .clip(RoundedCornerShape(16.dp))
                .background(colors.surface)
                .padding(28.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text("Parental check", style = MaterialTheme.typography.headlineSmall, color = colors.onSurface)
            Text(
                prompt,
                style = MaterialTheme.typography.bodyMedium,
                color = colors.onSurface,
                modifier = Modifier.padding(top = 8.dp)
            )
            Text(
                "$a × $b = ?",
                style = MaterialTheme.typography.headlineMedium,
                color = colors.onSurface,
                modifier = Modifier.padding(top = 16.dp)
            )
            OutlinedInput(
                value = answer,
                onValueChange = { new -> answer = new.filter(Char::isDigit); wrong = false },
                label = "Answer",
                keyboardType = KeyboardType.Number,
                imeAction = ImeAction.Done,
                modifier = Modifier.fillMaxWidth().padding(top = 12.dp)
            )
            if (wrong) {
                Text(
                    "That's not right — here's a new one.",
                    style = MaterialTheme.typography.labelMedium,
                    color = colors.primary,
                    modifier = Modifier.padding(top = 8.dp)
                )
            }
            Row(
                modifier = Modifier.padding(top = 20.dp),
                horizontalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                Button(onClick = {
                    if (answer.toIntOrNull() == a * b) onPass() else { wrong = true; round++ }
                }) { Text("OK") }
                Button(onClick = onCancel) { Text("Cancel") }
            }
        }
    }
}
