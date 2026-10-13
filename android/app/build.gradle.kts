plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.opencleaner.android"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.opencleaner.android"
        minSdk = 30
        targetSdk = 34
        versionCode = 12
        versionName = "1.3"
    }

    // Two APKs from one codebase: the phone app (Android 11+) and the TV app for Google TV / Android TV
    // and Fire TV (Android 9+, remote-friendly layout, its own application id).
    flavorDimensions += "device"
    productFlavors {
        create("phone") {
            dimension = "device"
            minSdk = 30
            buildConfigField("boolean", "IS_TV", "false")
            buildConfigField("String", "APK_SUFFIX", "\"-android.apk\"")
        }
        create("tv") {
            dimension = "device"
            applicationId = "com.opencleaner.tv"
            minSdk = 28
            buildConfigField("boolean", "IS_TV", "true")
            buildConfigField("String", "APK_SUFFIX", "\"-androidtv.apk\"")
        }
    }

    buildTypes {
        release { isMinifyEnabled = false }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { compose = true; buildConfig = true }
}

dependencies {
    implementation(platform("androidx.compose:compose-bom:2024.09.03"))
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.activity:activity-compose:1.9.2")
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.6")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.6")
}
