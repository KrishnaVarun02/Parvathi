import SwiftUI

// SDKs shipping both the State macro and the original property wrapper may choose the
// macro even for @State<Value>. This alias explicitly selects the stable wrapper and
// lets Command Line Tools builds work without Xcode's SwiftUIMacros compiler plugin.
typealias ViewState<Value> = SwiftUI.State<Value>
