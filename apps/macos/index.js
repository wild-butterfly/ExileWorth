import { AppRegistry } from 'react-native';
import { SettingsRoot } from './src/SettingsRoot';
import { OverlayRoot } from './src/overlay/OverlayRoot';

AppRegistry.registerComponent('ExileWorthSettings', () => SettingsRoot);
AppRegistry.registerComponent('ExileWorthOverlay', () => OverlayRoot);
