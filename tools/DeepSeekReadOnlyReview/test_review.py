import contextlib
from datetime import date
from decimal import Decimal
import io
from pathlib import Path
import socket
import tempfile
import unittest
from unittest.mock import patch
import review

class ReviewTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.ledger = Path(self.temp.name) / 'reservation.json'
        self.pricing = {'today': date(2026, 10, 28)}

    def test_budget_and_request(self):
        body, reserve = review.prepare('code', **self.pricing)
        self.assertEqual(Decimal('2.216384'), reserve)
        self.assertEqual({'type': 'disabled'}, body['thinking'])
        self.assertEqual('none', body['reasoning_effort'])
        self.assertEqual(2048, body['max_tokens'])
        self.assertNotIn('tools', body)
        self.assertNotIn('Authorization', str(body))

    def test_stale_price_fails_closed(self):
        with self.assertRaises(review.ReviewBlocked):
            review.prepare('code', today=date(2026, 10, 29))

    def test_budget_and_bad_price_rejected(self):
        for rate in [Decimal('5'), Decimal('NaN'), Decimal('-1'), Decimal('Infinity')]:
            with self.subTest(rate=rate), self.assertRaises(review.ReviewBlocked):
                review.prepare('code', input_rate=rate, **self.pricing)

    def test_unicode_and_empty_input_bound(self):
        for packet in ['', ' ', '光' * 5000]:
            with self.subTest(size=len(packet)), self.assertRaises(review.ReviewBlocked):
                review.prepare(packet, **self.pricing)

    def test_real_packet_offline(self):
        packet = Path(review.__file__).with_name('input.txt').read_text()
        with patch.object(socket, 'socket', side_effect=AssertionError('Network forbidden')):
            result = review.OneShotReview(self.ledger).run_mock(packet, review.OfflineMock(), **self.pricing)
        self.assertEqual('0', result['charged_cny'])

    def test_persistent_no_retry_after_success(self):
        transport = review.OfflineMock()
        review.OneShotReview(self.ledger).run_mock('code', transport, **self.pricing)
        with self.assertRaises(review.ReviewBlocked):
            review.OneShotReview(self.ledger).run_mock('code', transport, **self.pricing)
        self.assertEqual(1, transport.calls)

    def test_redirect_and_http_error_no_retry(self):
        for status in [301, 302, 307, 308, 401, 429, 500]:
            with self.subTest(status=status):
                ledger = self.ledger.with_name(str(status))
                transport = review.OfflineMock(status=status)
                for _ in range(2):
                    with self.assertRaises(review.ReviewBlocked):
                        review.OneShotReview(ledger).run_mock('code', transport, **self.pricing)
                self.assertEqual(1, transport.calls)

    def test_uncertain_error_redacted_and_reserved(self):
        transport = review.OfflineMock(failure=True)
        output = io.StringIO()
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            with self.assertRaises(review.ReviewBlocked) as caught:
                review.OneShotReview(self.ledger).run_mock('code', transport, **self.pricing)
        self.assertNotIn('synthetic sensitive', str(caught.exception))
        self.assertEqual('', output.getvalue())
        with self.assertRaises(review.ReviewBlocked):
            review.OneShotReview(self.ledger).run_mock('code', transport, **self.pricing)
        self.assertEqual(1, transport.calls)

    def test_transport_replacement_blocked(self):
        class ExternalTransport:
            def post(self, *args, **kwargs):
                self.fail('must never run')
        with self.assertRaises(review.ReviewBlocked):
            review.OneShotReview(self.ledger).run_mock('code', ExternalTransport(), **self.pricing)
        self.assertFalse(self.ledger.exists())

    def test_invalid_input_does_not_consume_reservation(self):
        with self.assertRaises(review.ReviewBlocked):
            review.OneShotReview(self.ledger).run_mock('', review.OfflineMock(), **self.pricing)
        self.assertFalse(self.ledger.exists())

if __name__ == '__main__':
    unittest.main()
