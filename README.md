# SusDr2Converter

The goal of this program is to provide compatibility between the *.sus format and the *.dr2 format. 
[*.sus](https://seaurchin.kb10uy.org/wiki/score/format) is the notechart format for [Seaurchin](https://github.com/kb10uy/Seaurchin); *.dr2 is the notechart format for Laverita.

Conversion is automatic and does not require user-input, aside from inputting the filenames. Running the *.exe alone provides a help menu.

## Releases

The current version is [v0.1](https://github.com/veroxzik/SusDr2Converter/releases/).

## Limitations

### On the Converter

The following limitations on v0.1 (2019-01-11) are as follows:

The program will only convert *.sus -> *.dr2
It does not work in the opposite direction. It is planned, but not a priority for me at the moment.

### On the Formats

The two formats do not feature identical functionality. The accuracy of this section is pending a formal specification of *.dr2 to be released.

*.sus format includes Bezier-curve slides. During play, these notes will appear as smooth curves. In the translation to *.dr2, these notes are turned into normal slides with straight segments.

*.sus format includes the ability to group sections of notes to have their own dedicated speed modifiers and visible/invisible tags. Most of the #TIL tag functionality is not supported at this time.

*.sus's #ATR and #ATTRIBUTE tag, which allows for custom draw priority, setting the height of air actions, and additional air action-related high speed mods, is not supported in *.dr2.

*.dr2 format includes the ability for damage/bomb notes to be slides.

When the converter comes across a known limitation, there is an error warning produced.

## License

This is released under the MIT license. Software is provded as-is with no warranty.