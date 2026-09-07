import 'package:flutter_test/flutter_test.dart';
import 'package:findupto_pos_mobile/main.dart';

void main() {
  testWidgets('FindUpTo POS app starts', (tester) async {
    await tester.pumpWidget(const PosMobileApp());
    expect(find.text('FindUpTo POS'), findsOneWidget);
  });
}
