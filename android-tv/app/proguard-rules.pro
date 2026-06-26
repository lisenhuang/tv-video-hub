# Keep generated kotlinx.serialization serializers and the DTOs they reference.
# kotlinx.serialization relies on synthetic companion + $serializer members.
-keepattributes *Annotation*, InnerClasses
-dontnote kotlinx.serialization.**

# Keep the @Serializable classes and their companion serializers.
-keepclassmembers class **$$serializer { *; }
-keepclassmembers @kotlinx.serialization.Serializable class * {
    *** Companion;
    *** serializer(...);
}
-keep class com.tvvideohub.tv.data.dto.** { *; }

# Retrofit / OkHttp baseline rules.
-dontwarn okhttp3.**
-dontwarn okio.**
-dontwarn retrofit2.**
-keepattributes Signature
-keepattributes Exceptions

# Media3 reflection.
-dontwarn androidx.media3.**
