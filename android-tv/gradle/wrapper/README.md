# Gradle wrapper

This directory intentionally contains only `gradle-wrapper.properties`.

The wrapper binary (`gradle-wrapper.jar`) and the `gradlew` / `gradlew.bat`
launcher scripts are **not committed** here, because they are platform/binary
artifacts. They are generated on demand:

- **In CI** the build runs `gradle wrapper --gradle-version 8.11.1` (or invokes the
  preinstalled system `gradle` directly), which regenerates `gradlew`,
  `gradlew.bat`, and `gradle/wrapper/gradle-wrapper.jar` from the version pinned
  in `gradle-wrapper.properties`.
- **Locally**, run the same command once if you want a self-contained `./gradlew`:

  ```sh
  cd android-tv
  gradle wrapper --gradle-version 8.11.1
  ```

After that, use `./gradlew :app:assembleDebug` (or `assembleRelease`) as usual.
