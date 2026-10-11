package com.opencleaner.android

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight

// OpenCleaner brand (docs/brand/README.md)
private val TrustBlue = Color(0xFF2563EB)
private val Sky = Color(0xFF3A86FF)
private val Deep = Color(0xFF1D4ED8)
private val Ink = Color(0xFF0B1020)
private val Slate = Color(0xFF5B6478)
private val Cloud = Color(0xFFF2F4F8)

private val Ubuntu = FontFamily(
    Font(R.font.ubuntu_regular, FontWeight.Normal),
    Font(R.font.ubuntu_medium, FontWeight.Medium),
)

private val LightColors = lightColorScheme(
    primary = TrustBlue, onPrimary = Color.White,
    primaryContainer = Color(0xFFDBE6FD), onPrimaryContainer = Deep,
    secondaryContainer = Color(0xFFDBE6FD), onSecondaryContainer = Deep,
    background = Cloud, onBackground = Ink,
    surface = Cloud, onSurface = Ink, onSurfaceVariant = Slate,
)

private val DarkColors = darkColorScheme(
    primary = Sky, onPrimary = Ink,
    primaryContainer = Deep, onPrimaryContainer = Color.White,
    secondaryContainer = Deep, onSecondaryContainer = Color.White,
    background = Ink, onBackground = Cloud,
    surface = Ink, onSurface = Cloud, onSurfaceVariant = Color(0xFFA3ABBD),
)

private fun typography(): Typography {
    val d = Typography()
    // Medium (500) for the name and headings, Regular (400) for body
    fun heading(s: androidx.compose.ui.text.TextStyle) =
        s.copy(fontFamily = Ubuntu, fontWeight = FontWeight.Medium)
    fun body(s: androidx.compose.ui.text.TextStyle) = s.copy(fontFamily = Ubuntu)
    return Typography(
        displayLarge = heading(d.displayLarge), displayMedium = heading(d.displayMedium),
        displaySmall = heading(d.displaySmall), headlineLarge = heading(d.headlineLarge),
        headlineMedium = heading(d.headlineMedium), headlineSmall = heading(d.headlineSmall),
        titleLarge = heading(d.titleLarge), titleMedium = heading(d.titleMedium),
        titleSmall = heading(d.titleSmall), bodyLarge = body(d.bodyLarge),
        bodyMedium = body(d.bodyMedium), bodySmall = body(d.bodySmall),
        labelLarge = body(d.labelLarge), labelMedium = body(d.labelMedium),
        labelSmall = body(d.labelSmall),
    )
}

@Composable
fun OpenCleanerTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = if (isSystemInDarkTheme()) DarkColors else LightColors,
        typography = typography(),
        content = content,
    )
}
